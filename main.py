import warnings
import os
import logging
# Suppress warnings before importing libraries
os.environ['TF_CPP_MIN_LOG_LEVEL'] = '2'  # Suppress TensorFlow warnings
os.environ['GLOG_minloglevel'] = '2'  # Suppress glog (used by MediaPipe/absl)
warnings.filterwarnings('ignore', category=UserWarning, module='google.protobuf')
warnings.filterwarnings('ignore', message='.*SymbolDatabase.GetPrototype.*')
# Suppress absl logging
logging.getLogger('absl').setLevel(logging.ERROR)

import cv2
import numpy as np
from ultralytics import YOLO
import subprocess
import onnxruntime
import winreg
import json
import re
import tkinter as tk
from tkinter import ttk, messagebox
import threading
import ctypes
from ctypes import wintypes
from PIL import Image, ImageDraw
import pystray
import time
import sys

# --- Constants ---
# Detection confidence threshold
CONF_THRESHOLD = 0.45
# Number of consecutive frames to trigger action
CONSECUTIVE_FRAMES_TRIGGER = 3
# Number of consecutive frames to idle
CONSECUTIVE_FRAMES_IDLE = 100

# --- YOLO Model Configuration ---
# You can change the model variant here.
# 'n' (nano) is the fastest but least accurate.
# 's' (small) is a good balance of speed and accuracy.
# The script will automatically download the required .pt file if it's missing.
MODEL_VARIANT = 'n' # Options: 'n', 's', 'm', 'l', 'x'
YOLO_MODEL_PT = f'yolov8{MODEL_VARIANT}.pt'
YOLO_MODEL_ONNX = f'yolov8{MODEL_VARIANT}.onnx'


# --- Audio Control Configuration ---
# IMPORTANT: 
# 1. Download AudioPlaybackConnector from https://github.com/ysc3839/AudioPlaybackConnector/releases
# 2. Unzip it and place AudioPlaybackConnector.exe in the same directory as this script,
#    or provide the full path below.
# 3. Replace with your phone's Bluetooth MAC address. You can find this in your
#    phone's Bluetooth settings ("Bluetooth address" or similar).
AUDIO_CONNECTOR_PATH = "AudioPlaybackConnector64.exe"
PHONE_BT_ADDRESS = "EC:AA:25:93:4D:48" # <-- IMPORTANT: CHANGE THIS
AUDIO_COMMAND_TIMEOUT_SEC = 30

# Config file to persist selected Bluetooth address
CONFIG_PATH = "bt_config.json"


# --- Bluetooth Device Resolution ---
def _format_mac_from_registry_key(registry_key_name):
    """
    Windows stores paired device addresses as a 12-hex-character key under
    HKLM\\SYSTEM\\CurrentControlSet\\Services\\BTHPORT\\Parameters\\Devices.
    The byte order is typically reversed. Convert to AA:BB:CC:DD:EE:FF.
    """
    hex_str = registry_key_name.strip().replace(":", "").replace("-", "").upper()
    if len(hex_str) != 12 or any(c not in "0123456789ABCDEF" for c in hex_str):
        return None
    pairs = [hex_str[i:i+2] for i in range(0, 12, 2)]
    pairs.reverse()
    return ":".join(pairs)


def _get_paired_bluetooth_devices_windows():
    """
    Returns a list of (name, mac) for paired Bluetooth devices using registry.
    """
    devices = []
    try:
        with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r"SYSTEM\\CurrentControlSet\\Services\\BTHPORT\\Parameters\\Devices") as key:
            index = 0
            while True:
                try:
                    subkey_name = winreg.EnumKey(key, index)
                    index += 1
                except OSError:
                    break

                mac = _format_mac_from_registry_key(subkey_name)
                
                # If MAC conversion failed, try using the subkey_name directly as unique identifier
                if not mac and len(subkey_name) == 12:
                    # Format as MAC address: AABBCCDDEEFF -> AA:BB:CC:DD:EE:FF
                    try:
                        mac = ":".join(subkey_name[i:i+2] for i in range(0, 12, 2)).upper()
                    except:
                        pass
                
                name = None
                try:
                    with winreg.OpenKey(key, subkey_name) as dev_key:
                        # The 'Name' value is typically REG_BINARY with UTF-16LE content
                        name_raw, _ = winreg.QueryValueEx(dev_key, "Name")
                        if isinstance(name_raw, (bytes, bytearray)):
                            try:
                                # Decode UTF-16LE and strip null bytes
                                decoded = name_raw.decode("utf-16le", errors="replace").strip("\x00").strip()
                                
                                # Check if the decoded string is readable (mostly ASCII/printable)
                                if decoded:
                                    # Count printable ASCII characters
                                    printable_count = sum(1 for c in decoded if 32 <= ord(c) <= 126 or c in '\n\r\t')
                                    total_chars = len(decoded)
                                    
                                    # If at least 50% is printable ASCII, consider it valid
                                    if total_chars > 0 and (printable_count / total_chars) >= 0.5:
                                        name = decoded
                                    else:
                                        # Garbled text - skip it
                                        name = None
                            except Exception:
                                name = None
                        elif isinstance(name_raw, str):
                            # Already a string
                            name = name_raw.strip()
                except OSError:
                    pass

                if mac:
                    devices.append((name or "(Unknown)", mac))
    except OSError:
        # Registry path not found or inaccessible
        pass

    # Deduplicate by MAC while keeping first seen name
    seen = set()
    unique_devices = []
    for n, m in devices:
        if m not in seen:
            unique_devices.append((n, m))
            seen.add(m)
    return unique_devices


def _load_saved_bt_address():
    try:
        if os.path.exists(CONFIG_PATH):
            with open(CONFIG_PATH, "r", encoding="utf-8") as f:
                data = json.load(f)
                addr = data.get("phone_bt_address")
                if isinstance(addr, str):
                    return addr
    except Exception:
        pass
    return None


def _load_webcam_index():
    """Load webcam index from config file. Returns 0 if not found or invalid."""
    try:
        if os.path.exists(CONFIG_PATH):
            with open(CONFIG_PATH, "r", encoding="utf-8") as f:
                data = json.load(f)
                idx = data.get("webcam_index")
                if isinstance(idx, int) and idx >= 0:
                    return idx
                # Also try as string for backwards compatibility
                if isinstance(idx, str):
                    try:
                        idx_int = int(idx)
                        if idx_int >= 0:
                            return idx_int
                    except ValueError:
                        pass
    except Exception:
        pass
    return 0


def _save_bt_address(address):
    try:
        with open(CONFIG_PATH, "w", encoding="utf-8") as f:
            json.dump({"phone_bt_address": address}, f, ensure_ascii=False, indent=2)
    except Exception:
        pass


# Minimal helpers used by selection and audio flow
def _check_bt_address_exists():
    """
    Returns True if a Bluetooth address is already configured via saved config
    or hardcoded constant, False otherwise.
    """
    saved = _load_saved_bt_address()
    if saved:
        return True
    if PHONE_BT_ADDRESS and PHONE_BT_ADDRESS != "XX:XX:XX:XX:XX:XX":
        return True
    return False


def resolve_phone_bt_address():
    """
    Resolve the phone Bluetooth MAC address from saved config or constant.
    Returns address string or None.
    """
    saved = _load_saved_bt_address()
    if saved:
        return saved
    if PHONE_BT_ADDRESS and PHONE_BT_ADDRESS != "XX:XX:XX:XX:XX:XX":
        return PHONE_BT_ADDRESS
    return None


# --- Bluetooth Device Selector UI ---
# This section has been updated to use the native Windows Device Picker,
# providing a better user experience than the previous custom Tkinter window.
from winsdk.windows.devices.enumeration import DeviceInformation
import asyncio

def _extract_mac_from_device_id(device_id):
    """
    Extracts a Bluetooth MAC address from a Windows device ID string.
    The ID for Bluetooth devices typically contains the MAC address as a 12-character hex string.
    Example: ...BTHENUM\\...\\...&2f934deb&0&ECAA25934D48_C00000000
    """
    if not device_id:
        return None
    # Search for a 12-character hexadecimal string, which is the standard MAC address format in device IDs.
    match = re.search(r'([0-9A-Fa-f]{12})', device_id)
    if match:
        mac_str = match.group(1)
        # Format the 12-character string into a standard MAC address format (XX:XX:XX:XX:XX:XX)
        return ":".join(mac_str[i:i+2] for i in range(0, 12, 2)).upper()
    return None

async def _enumerate_a2dp_devices_async():
    """Return a list of (name, mac, id) for A2DP sink devices using Windows APIs."""
    try:
        selector = AudioPlaybackConnection.get_device_selector()
        devices = await DeviceInformation.find_all_async(selector, [])
        results = []
        for dev in devices:
            dev_id = str(dev.id)
            name = str(dev.name) if dev.name is not None else "(Unknown)"
            mac = _extract_mac_from_device_id(dev_id)
            if mac:
                results.append((name if name else "(Unknown)", mac, dev_id))
        return results
    except Exception as e:
        print(f"Failed to enumerate A2DP devices: {e}")
        return []

def show_bluetooth_selector_ui():
    """
    Manages the Bluetooth device selection process at startup.
    - Checks for an existing configuration.
    - Asks the user if they want to change it.
    - Shows the native Windows device picker if needed.
    Returns the configured MAC address.
    """
    # First, check if a device address is already saved in the config file.
    if _check_bt_address_exists():
        root = tk.Tk()
        root.withdraw()  # Hide the root Tkinter window for the messagebox.
        
        current_addr = _load_saved_bt_address()
        change = False
        if current_addr:
            # If a device is already configured, ask the user if they want to change it.
            change = messagebox.askyesno(
                "Bluetooth Already Configured",
                f"The application is configured to use this Bluetooth device:\n\n{current_addr}\n\nDo you want to select a different one?"
            )
        
        root.destroy()
        
        if not change:
            # If the user doesn't want to change, we're done. Return the current address.
            return current_addr

    # If no device is configured or the user wants to change it, enumerate and show a simple selector.
    devices = asyncio.run(_enumerate_a2dp_devices_async())
    
    # Build a minimal Tk dialog listing names with MACs
    dlg = tk.Tk()
    dlg.title("Bluetooth Device Selector")
    dlg.geometry("600x400")
    dlg.resizable(False, False)
    
    tk.Label(dlg, text="Select Your Phone's Bluetooth Device", font=("Arial", 14, "bold")).pack(pady=16)
    tk.Label(dlg, text="Choose the device you want to use for audio control:", font=("Arial", 10)).pack(pady=4)
    frame = tk.Frame(dlg)
    frame.pack(padx=16, pady=8, fill=tk.BOTH, expand=True)
    scrollbar = tk.Scrollbar(frame)
    scrollbar.pack(side=tk.RIGHT, fill=tk.Y)
    listbox = tk.Listbox(frame, yscrollcommand=scrollbar.set, font=("Courier New", 10), selectmode=tk.SINGLE, height=12)
    listbox.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
    scrollbar.config(command=listbox.yview)
    
    entries = []
    for name, mac, dev_id in devices:
        entries.append((name, mac, dev_id))
        listbox.insert(tk.END, f"[{mac}]  {name}")
    
    selected_mac = {"value": None}
    selected_name = {"value": None}
    
    def on_select():
        sel = listbox.curselection()
        if not sel:
            messagebox.showwarning("No Selection", "Please select a Bluetooth device first.")
            return
        idx = sel[0]
        name, mac, _ = entries[idx]
        selected_mac["value"] = mac
        selected_name["value"] = name
        dlg.destroy()
    
    def on_skip():
        if messagebox.askyesno("Skip Configuration", "Audio control will be disabled. Continue?"):
            dlg.destroy()
    
    btns = tk.Frame(dlg)
    btns.pack(pady=10)
    tk.Button(btns, text="Select & Continue", command=on_select, font=("Arial", 11), bg="#4CAF50", fg="white", padx=20, pady=8).pack(side=tk.LEFT, padx=10)
    tk.Button(btns, text="Skip (No Audio Control)", command=on_skip, font=("Arial", 11), bg="#FF9800", fg="white", padx=20, pady=8).pack(side=tk.LEFT, padx=10)
    tk.Button(btns, text="Refresh", command=lambda: None, state=tk.DISABLED, font=("Arial", 11), bg="#2196F3", fg="white", padx=20, pady=8).pack(side=tk.LEFT, padx=10)
    
    dlg.mainloop()
    
    if selected_mac["value"]:
        _save_bt_address(selected_mac["value"])
        root = tk.Tk()
        root.withdraw()
        messagebox.showinfo("Device Configured", f"Successfully configured audio control for:\n\n{selected_name['value']}\n({selected_mac['value']})")
        root.destroy()
        return selected_mac["value"]
    else:
        return None


# --- Helper Functions ---
# (No helper functions needed - phone detection only)

# --- Audio Control ---
# New implementation using Python for Windows Runtime (winsdk)
# This replaces the need for the external AudioPlaybackConnector.exe
import asyncio
from winsdk.windows.devices.enumeration import DeviceInformation
from winsdk.windows.media.audio import (
    AudioPlaybackConnection,
    AudioPlaybackConnectionOpenResultStatus,
    AudioPlaybackConnectionState,
)

class BluetoothAudioConnector:
    """
    Manages a Bluetooth A2DP audio connection using Windows RT APIs.
    """

    def __init__(self):
        self.connection = None
        self.device_id = None
        self.is_connected = False
        # Simple reentrancy guard; all async calls are scheduled on a single loop
        self._busy = False

    async def _get_device_id_from_mac(self, mac_address):
        """Finds the full device ID for a given Bluetooth MAC address."""
        try:
            # Format MAC for searching (remove separators, uppercase)
            search_mac = mac_address.replace(":", "").replace("-", "").upper()

            # Get the selector for A2DP sink devices
            a2dp_sink_selector = AudioPlaybackConnection.get_device_selector()
            if not a2dp_sink_selector:
                print("Could not get A2DP Sink device selector.")
                return None
            
            # Find all devices matching the selector
            # Pass an empty list for the second arg to ensure the correct overload is chosen.
            devices = await DeviceInformation.find_all_async(a2dp_sink_selector, [])
            
            if not devices:
                print("No A2DP Sink devices found.")
                return None

            # Find the device whose ID contains the MAC address
            for device in devices:
                if search_mac in device.id.upper():
                    print(f"Found device: {device.name} [{device.id}]")
                    return device.id
            
            print(f"ERROR: No device found for MAC address {mac_address}")
            return None

        except Exception as e:
            print(f"Error finding device: {e}")
            return None

    def _on_state_changed(self, sender, arg):
        """Handles connection state changes."""
        state = sender.state
        print(f"Audio connection state changed to: {state.name}")
        if state == AudioPlaybackConnectionState.CLOSED:
            self.is_connected = False
            self.connection = None

    async def connect(self, mac_address):
        """Connects to the specified Bluetooth device."""
        if self._busy:
            return False
        self._busy = True
        try:
            if self.is_connected:
                print("Already connected.")
                return True

            # Find the full device ID
            if not self.device_id:
                self.device_id = await self._get_device_id_from_mac(mac_address)
            
            if not self.device_id:
                print("Could not connect: device ID not found.")
                return False

            print("Attempting to connect...")
            try:
                # Create a new connection object
                self.connection = AudioPlaybackConnection.try_create_from_id(self.device_id)

                if not self.connection:
                    print("Failed to create AudioPlaybackConnection.")
                    return False

                # Register for state change events
                self.connection.add_state_changed(self._on_state_changed)
                
                # Start and open the connection
                await self.connection.start_async()
                result = await self.connection.open_async()
                
                if result.status == AudioPlaybackConnectionOpenResultStatus.SUCCESS:
                    print("Successfully connected to audio device.")
                    self.is_connected = True
                    return True
                else:
                    print(f"Failed to connect: {result.status.name} (Error: {result.extended_error})")
                    if self.connection:
                        self.connection.close()
                    self.connection = None
                    return False

            except Exception as e:
                print(f"Exception during connection: {e}")
                if self.connection:
                    self.connection.close()
                self.connection = None
                return False
        finally:
            self._busy = False

    async def disconnect(self):
        """Disconnects the current audio connection."""
        if self._busy:
            return False
        self._busy = True
        try:
            if not self.is_connected or not self.connection:
                print("Not connected.")
                return True
            
            print("Disconnecting...")
            try:
                self.connection.close()
                self.connection = None
                self.is_connected = False
                print("Disconnected successfully.")
                return True
            except Exception as e:
                print(f"Exception during disconnection: {e}")
                return False
        finally:
            self._busy = False

# Global audio connector instance
audio_connector = BluetoothAudioConnector()

# Single background asyncio loop to run connector coroutines
_bg_loop = None
_bg_thread = None

def _loop_runner(loop):
    asyncio.set_event_loop(loop)
    loop.run_forever()

def _ensure_background_loop():
    global _bg_loop, _bg_thread
    if _bg_loop is None or not _bg_loop.is_running():
        _bg_loop = asyncio.new_event_loop()
        _bg_thread = threading.Thread(target=_loop_runner, args=(_bg_loop,), name="AudioAsyncLoop", daemon=True)
        _bg_thread.start()


def run_audio_command(action):
    """
    Runs connect/disconnect action asynchronously in the background.
    """
    try:
        _ensure_background_loop()
        address = _load_saved_bt_address() # Use the saved address
        if not address:
            # Fallback for compatibility, though UI should handle this
            address = resolve_phone_bt_address()

        if not address:
            print("WARNING: No Bluetooth MAC address configured. Audio control disabled.")
            return

        if action == "connect":
            task = audio_connector.connect(address)
        elif action == "disconnect":
            task = audio_connector.disconnect()
        else:
            return

        # Schedule the coroutine on the persistent background loop
        future = asyncio.run_coroutine_threadsafe(task, _bg_loop)
        def _done_cb(f):
            try:
                f.result()
            except Exception as e:
                print(f"Error running async task in thread: {e}")
        future.add_done_callback(_done_cb)
        
    except Exception as e:
        print(f"Failed to run audio command: {e}")


def connect_audio():
    """Connects the Bluetooth A2DP sink."""
    print("ACTION: Connecting Bluetooth audio...")
    run_audio_command("connect")

def disconnect_audio():
    """Disconnects the Bluetooth A2DP sink."""
    print("ACTION: Disconnecting Bluetooth audio...")
    run_audio_command("disconnect")


# --- System Tray Icon ---
# Global variables for system tray
tray_icon = None
app_running = True

# Modes
detection_enabled = False  # Automatic detection (camera) mode is OFF by default
webcam_index = 0  # Webcam index (0 by default, can be changed via config or command)
keybind_enabled = True    # Manual keybind mode (Ctrl+Alt+C)
cap = None  # Global video capture object

# Manual override state driven by Ctrl+Alt+C when keybind_enabled
# Values: None (no override), 'on' (force on), 'off' (prefer off but allow auto to turn on)
manual_override_state = None

app_status = {
    "phone_detected": False,
    "audio_active": False,
    "detection_enabled": True,
    "keybind_enabled": True,
    "manual_override": None
}

def create_tray_icon():
    """Create a simple icon for the system tray."""
    # Create a simple icon image (headphone icon)
    width = 64
    height = 64
    image = Image.new('RGB', (width, height), color='black')
    dc = ImageDraw.Draw(image)
    
    # Draw a simple headphone icon
    # Left ear cup
    dc.ellipse([8, 20, 22, 34], fill='white', outline='white')
    # Right ear cup
    dc.ellipse([42, 20, 56, 34], fill='white', outline='white')
    # Headband
    dc.arc([12, 10, 52, 30], start=0, end=180, fill='white', width=3)
    
    return image

def get_device_name():
    """Get configured device name from config."""
    try:
        if os.path.exists(CONFIG_PATH):
            with open(CONFIG_PATH, "r", encoding="utf-8") as f:
                data = json.load(f)
                addr = data.get("phone_bt_address", "")
                if addr:
                    # Try to get device name from paired devices
                    devices = _get_paired_bluetooth_devices_windows()
                    for name, mac in devices:
                        if mac.upper() == addr.upper():
                            return name
                    return addr  # Return MAC if name not found
    except:
        pass
    return "Not configured"

def get_status_text():
    """Get current connection status text."""
    device_name = get_device_name()
    if app_status.get('audio_active', False):
        return f"✓ Connected to {device_name}"
    else:
        return f"○ Disconnected ({device_name})"

def get_connect_disconnect_text():
    """Get text for connect/disconnect menu item."""
    if app_status.get('audio_active', False):
        return "Disconnect Audio"
    else:
        return "Connect Audio"

def get_auto_detection_text():
    """Get text for auto detection menu item."""
    if app_status.get('detection_enabled', True):
        return "✓ Auto Detection"
    else:
        return "○ Auto Detection"

def on_show_status(icon, item):
    """Show current status in a message box."""
    status_msg = f"Detection Enabled: {'Yes' if app_status['detection_enabled'] else 'No'}\n"
    status_msg += f"Phone Detected: {'Yes' if app_status['phone_detected'] else 'No'}\n"
    status_msg += f"Audio Active: {'Yes' if app_status['audio_active'] else 'No'}"
    
    root = tk.Tk()
    root.withdraw()
    messagebox.showinfo("HoldSense Status", status_msg)
    root.destroy()

def on_toggle_audio(icon, item):
    """Toggle audio connection manually."""
    toggle_audio_connection()
    # Update icon to reflect changes
    if icon:
        icon.title = get_status_text()
        icon.update_menu()

def on_toggle_detection(icon, item):
    """Toggle webcam detection on/off."""
    toggle_webcam_detection()
    # Update icon to reflect changes
    if icon:
        icon.title = get_status_text()
        icon.update_menu()

def on_quit(icon, item):
    """Exit the application."""
    global app_running
    app_running = False
    icon.stop()

def setup_tray_icon():
    """Setup and return the system tray icon."""
    icon_image = create_tray_icon()
    
    # Create menu with dynamic status display
    menu = pystray.Menu(
        # Status display (informational)
        lambda: pystray.MenuItem(get_status_text(), None, enabled=False),
        pystray.Menu.SEPARATOR,
        # Connect/Disconnect
        lambda: pystray.MenuItem(get_connect_disconnect_text(), on_toggle_audio),
        # Auto Detection Toggle
        lambda: pystray.MenuItem(get_auto_detection_text(), on_toggle_detection),
        pystray.Menu.SEPARATOR,
        # Additional options
        pystray.MenuItem("Show Detailed Status", on_show_status),
        pystray.Menu.SEPARATOR,
        # Exit
        pystray.MenuItem("Exit", on_quit)
    )
    
    icon = pystray.Icon("HoldSense", icon_image, get_status_text(), menu)
    return icon

def run_tray_icon():
    """Run the system tray icon in a separate thread."""
    global tray_icon
    tray_icon = setup_tray_icon()
    
    # Start a background thread to update the tray icon periodically
    def update_tray_status():
        while app_running and tray_icon:
            try:
                time.sleep(2)  # Update every 2 seconds
                if tray_icon:
                    tray_icon.title = get_status_text()
                    tray_icon.update_menu()
            except:
                break
    
    update_thread = threading.Thread(target=update_tray_status, daemon=True)
    update_thread.start()
    
    tray_icon.run()

# --- Global Hotkey Support (Ctrl+Alt+C and Ctrl+Alt+W) ---
WM_HOTKEY = 0x0312
MOD_ALT = 0x0001
MOD_CONTROL = 0x0002
MOD_SHIFT = 0x0004
VK_C = 0x43
VK_W = 0x57

def toggle_audio_connection():
    """Toggle connect/disconnect based on current state."""
    global manual_override_state
    try:
        if not keybind_enabled:
            print("HOTKEY: Keybind mode disabled; ignoring Ctrl+Alt+C")
            return
        if audio_connector.is_connected:
            print("HOTKEY: Toggling OFF (disconnect)")
            manual_override_state = 'off'
            run_audio_command("disconnect")
        else:
            print("HOTKEY: Toggling ON (connect)")
            manual_override_state = 'on'
            run_audio_command("connect")
        app_status["manual_override"] = manual_override_state
    except Exception as e:
        print(f"Toggle error: {e}")

def toggle_webcam_detection():
    """Toggle webcam detection on/off."""
    global detection_enabled
    detection_enabled = not detection_enabled
    app_status["detection_enabled"] = detection_enabled
    status_str = "ENABLED" if detection_enabled else "DISABLED"
    print(f"HOTKEY: Webcam detection {status_str}")

def _hotkey_listener_thread():
    """Background thread that registers global hotkeys and listens for presses."""
    user32 = ctypes.windll.user32
    
    # Register Ctrl+Alt+C for audio toggle
    if not user32.RegisterHotKey(None, 1, MOD_CONTROL | MOD_ALT, VK_C):
        print("WARNING: Failed to register global hotkey Ctrl+Alt+C")
    
    # Register Ctrl+Alt+W for detection toggle
    if not user32.RegisterHotKey(None, 2, MOD_CONTROL | MOD_ALT, VK_W):
        print("WARNING: Failed to register global hotkey Ctrl+Alt+W")
    
    try:
        msg = wintypes.MSG()
        while app_running:
            ret = user32.GetMessageW(ctypes.byref(msg), None, 0, 0)
            if ret == 0 or ret == -1:
                break
            if msg.message == WM_HOTKEY:
                if msg.wParam == 1:  # Ctrl+Alt+C
                    toggle_audio_connection()
                elif msg.wParam == 2:  # Ctrl+Alt+W
                    toggle_webcam_detection()
            user32.TranslateMessage(ctypes.byref(msg))
            user32.DispatchMessageW(ctypes.byref(msg))
    finally:
        user32.UnregisterHotKey(None, 1)
        user32.UnregisterHotKey(None, 2)

def start_hotkey_listener():
    """Start the global hotkey listener in a daemon thread."""
    try:
        t = threading.Thread(target=_hotkey_listener_thread, name="HotkeyListener", daemon=True)
        t.start()
    except Exception as e:
        print(f"Failed to start hotkey listener: {e}")

# --- Command Handler for C# Communication ---
def handle_stdin_commands():
    """Background thread that listens for commands from stdin (C# app)."""
    global detection_enabled, app_running, webcam_index, cap
    
    while app_running:
        try:
            if sys.stdin and not sys.stdin.closed:
                line = sys.stdin.readline()
                if not line:
                    break
                    
                command = line.strip().lower()
                
                if command == "toggle_detection":
                    toggle_webcam_detection()
                    print(f"STATUS:detection_enabled:{detection_enabled}", flush=True)
                elif command == "toggle_audio":
                    toggle_audio_connection()
                elif command == "disconnect_audio":
                    run_audio_command("disconnect")
                elif command == "get_status":
                    print(f"STATUS:detection_enabled:{detection_enabled}", flush=True)
                    print(f"STATUS:phone_detected:{app_status.get('phone_detected', False)}", flush=True)
                    print(f"STATUS:audio_active:{app_status.get('audio_active', False)}", flush=True)
                    print(f"STATUS:keybind_enabled:{keybind_enabled}", flush=True)
                    print(f"STATUS:manual_override:{manual_override_state}", flush=True)
                elif command.startswith("set_keybind_enabled:"):
                    value = command.split(":", 1)[1].strip()
                    new_val = value in ("1", "true", "yes")
                    print(f"INFO: Setting keybind_enabled={new_val}")
                    globals()["keybind_enabled"] = new_val
                    app_status["keybind_enabled"] = new_val
                elif command.startswith("set_auto_enabled:"):
                    value = command.split(":", 1)[1].strip()
                    new_val = value in ("1", "true", "yes")
                    print(f"INFO: Setting detection_enabled={new_val}")
                    globals()["detection_enabled"] = new_val
                    app_status["detection_enabled"] = new_val
                elif command.startswith("set_webcam_index:"):
                    try:
                        idx_str = command.split(":", 1)[1].strip()
                        new_idx = int(idx_str)
                        if new_idx >= 0:
                            print(f"INFO: Setting webcam_index={new_idx}")
                            globals()["webcam_index"] = new_idx
                            # Close current camera if open, so it will reopen with new index
                            if cap is not None and cap.isOpened():
                                try:
                                    cap.release()
                                except Exception:
                                    pass
                                cap = None
                            print(f"INFO: Camera will be reopened with index {new_idx} on next detection cycle")
                        else:
                            print(f"WARNING: Invalid webcam index {new_idx}, must be >= 0")
                    except (ValueError, IndexError) as e:
                        print(f"ERROR: Failed to parse webcam index: {e}")
                elif command == "clear_manual_override":
                    globals()["manual_override_state"] = None
                    app_status["manual_override"] = None
                elif command == "exit":
                    app_running = False
                    break
        except Exception as e:
            # Silently handle errors (stdin might not be available)
            break

# --- Main Application Logic ---
def main():
    """
    Main function to run phone detection and audio control.
    Detects phones using YOLO and connects/disconnects Bluetooth audio accordingly.
    """
    # Check if running with --no-ui flag (launched by Avalonia app)
    no_ui_mode = '--no-ui' in sys.argv
    
    if not no_ui_mode:
        # Show Bluetooth device selector UI at startup (only in standalone mode)
        print("Loading Bluetooth configuration UI...")
        selected_address = show_bluetooth_selector_ui()
    else:
        print("Running in no-UI mode (managed by Avalonia app)...")
        # Check if config exists
        if not _check_bt_address_exists():
            print("ERROR: No Bluetooth device configured. Please configure via the Avalonia UI.")
            return
        
        # Start stdin command handler for C# communication
        stdin_thread = threading.Thread(target=handle_stdin_commands, name="StdinHandler", daemon=True)
        stdin_thread.start()
    
    print("Starting the application...")

    # --- Initialization ---
    # Load webcam index from config
    global webcam_index, cap
    webcam_index = _load_webcam_index()
    print(f"INFO: Using webcam index {webcam_index}")

    # Prepare video capture with backend fallbacks (Windows MSMF may fail)
    cap = None
    backends_to_try = [getattr(cv2, 'CAP_DSHOW', 700), getattr(cv2, 'CAP_MSMF', 1400), getattr(cv2, 'CAP_ANY', 0)]
    def try_open_camera():
        global cap
        for backend in backends_to_try:
            try:
                cap = cv2.VideoCapture(webcam_index, backend)
                if cap.isOpened():
                    print(f"Camera opened at index {webcam_index} (backend={backend}).")
                    return True
                else:
                    if cap is not None:
                        cap.release()
            except Exception:
                if cap is not None:
                    cap.release()
                cap = None
        return False

    # --- ONNX Model (Lazy) ---
    session = None
    input_name = None
    input_height = None
    input_width = None

    def ensure_model_loaded():
        nonlocal session, input_name, input_height, input_width
        if session is not None:
            return True
        # Export model if needed
        if not os.path.exists(YOLO_MODEL_ONNX):
            print(f"ONNX model not found. Exporting {YOLO_MODEL_PT} to {YOLO_MODEL_ONNX}...")
            model_pt = YOLO(YOLO_MODEL_PT)
            model_pt.export(format='onnx', imgsz=640, opset=12)
            print("Export complete.")
        # Load model
        print("Loading ONNX YOLO model (lazy)...")
        try:
            session = onnxruntime.InferenceSession(YOLO_MODEL_ONNX, providers=['DmlExecutionProvider', 'CPUExecutionProvider'])
            print("ONNX model loaded with DmlExecutionProvider (GPU).")
        except Exception as e:
            print(f"Failed to load with DmlExecutionProvider: {e}")
            print("Loading with CPUExecutionProvider.")
            session = onnxruntime.InferenceSession(YOLO_MODEL_ONNX, providers=['CPUExecutionProvider'])
            print("ONNX model loaded with CPUExecutionProvider.")
        # Input details
        model_inputs = session.get_inputs()
        input_shape = model_inputs[0].shape
        input_name = model_inputs[0].name
        input_height, input_width = input_shape[2], input_shape[3]
        return True

    def release_model():
        nonlocal session, input_name, input_height, input_width
        if session is not None:
            try:
                s = session
                session = None
                input_name = None
                input_height = None
                input_width = None
                del s
                import gc
                gc.collect()
                print("ONNX model released.")
            except Exception:
                pass


    # --- State Variables ---
    detection_counter = 0
    idle_counter = 0
    audio_passthrough_active = False
    # Audio is available if an address was configured via UI
    audio_available = _check_bt_address_exists()
    if audio_available:
        current_addr = _load_saved_bt_address()
        print(f"INFO: Bluetooth audio control enabled for device: {current_addr}")

        # Pre-resolve the device ID in the background to speed up first connect
        def _prewarm_device_id(address: str):
            async def _run():
                try:
                    did = await audio_connector._get_device_id_from_mac(address)
                    audio_connector.device_id = did
                except Exception:
                    pass

            loop = asyncio.new_event_loop()
            try:
                asyncio.set_event_loop(loop)
                loop.run_until_complete(_run())
            finally:
                try:
                    loop.close()
                except Exception:
                    pass

        try:
            threading.Thread(target=_prewarm_device_id, args=(current_addr,), daemon=True).start()
        except Exception:
            pass
    else:
        print("INFO: Bluetooth address not configured. Audio control disabled.")

    # --- Main Loop ---
    # Only start system tray icon in standalone mode
    if not no_ui_mode:
        tray_thread = threading.Thread(target=run_tray_icon, name="TrayIcon", daemon=True)
        tray_thread.start()
        print("Application is now running in the background. Check system tray for status.")
    
    # Start global hotkey listener (Ctrl+Alt+C and Ctrl+Alt+W) to toggle connection
    start_hotkey_listener()
    
    while app_running:
        # If detection is disabled, ensure camera is released and skip processing
        if not detection_enabled:
            if cap is not None and cap.isOpened():
                try:
                    cap.release()
                except Exception:
                    pass
                cap = None
            # Release model to free memory and GPU
            release_model()
            # Reset counters and sleep briefly; only Ctrl+Alt+C remains functional
            detection_counter = 0
            idle_counter = 0
            app_status["detection_enabled"] = False
            cv2.waitKey(200)
            continue

        # Detection enabled: ensure camera is open
        if cap is None or not cap.isOpened():
            if not try_open_camera():
                cv2.waitKey(50)
                continue

        # Ensure model is loaded only when needed
        if session is None:
            if not ensure_model_loaded():
                cv2.waitKey(50)
                continue

        # Sync audio active state from connector
        audio_passthrough_active = bool(getattr(audio_connector, 'is_connected', False))

        # Read a frame from the camera
        success, frame = cap.read()
        if not success:
            print("Ignoring empty camera frame.")
            cv2.waitKey(10)
            continue
        
        # --- Pre-process for ONNX ---
        # Resize and pad the image to the model's expected input size
        h, w, _ = frame.shape
        scale = min(input_width / w, input_height / h)
        scaled_w, scaled_h = int(w * scale), int(h * scale)
        scaled_img = cv2.resize(frame, (scaled_w, scaled_h), interpolation=cv2.INTER_AREA)

        top_pad = (input_height - scaled_h) // 2
        bottom_pad = input_height - scaled_h - top_pad
        left_pad = (input_width - scaled_w) // 2
        right_pad = input_width - scaled_w - left_pad

        padded_img = cv2.copyMakeBorder(scaled_img, top_pad, bottom_pad, left_pad, right_pad, cv2.BORDER_CONSTANT, value=(114, 114, 114))
        
        # Convert to float, normalize, and transpose
        blob = cv2.dnn.blobFromImage(padded_img, 1/255.0, (input_width, input_height), swapRB=True, crop=False)


        # --- Perform Detections ---
        # Phone detection using ONNX Runtime
        outputs = session.run(None, {input_name: blob})

        # --- Process Detections ---
        phone_detected = False
        
        # Post-process YOLO output
        phone_boxes = []
        if outputs:
            # The output of YOLOv8 ONNX model is (batch, 84, 8400)
            # 84 = 4 (bbox) + 80 (classes)
            # 8400 = number of detections
            output_tensor = np.squeeze(outputs[0]).T # Transpose to (8400, 84)

            boxes = []
            confidences = []

            for row in output_tensor:
                # We are only interested in the 'cell phone' class (id 67)
                phone_confidence = row[4+67]
                if phone_confidence > CONF_THRESHOLD:
                    cx, cy, w_box, h_box = row[:4]
                    
                    # Convert from center to top-left
                    x1 = (cx - w_box/2)
                    y1 = (cy - h_box/2)
                    
                    boxes.append([x1, y1, w_box, h_box])
                    confidences.append(float(phone_confidence))
            
            # Non-Maximum Suppression
            indices = cv2.dnn.NMSBoxes(boxes, confidences, CONF_THRESHOLD, 0.5)

            if len(indices) > 0:
                for i in indices.flatten():
                    x, y, w_box, h_box = boxes[i]
                    
                    # Rescale to original frame size
                    # Remove padding
                    x = (x - left_pad) / scale
                    y = (y - top_pad) / scale
                    w_box = w_box / scale
                    h_box = h_box / scale

                    phone_boxes.append([int(x), int(y), int(x + w_box), int(y + h_box)])

        # Phone is detected if any phone boxes were found
        phone_detected = len(phone_boxes) > 0
        
        # --- Update State ---
        # Update global status for system tray and output status changes
        prev_audio_active = app_status.get("audio_active", False)
        prev_detection_enabled = app_status.get("detection_enabled", False)
        
        app_status["phone_detected"] = phone_detected
        app_status["audio_active"] = audio_passthrough_active
        app_status["detection_enabled"] = detection_enabled
        app_status["keybind_enabled"] = keybind_enabled
        app_status["manual_override"] = manual_override_state
        
        # Output status changes for C# tray icon (only in no-UI mode)
        if no_ui_mode:
            if prev_audio_active != audio_passthrough_active:
                print(f"STATUS:audio_active:{audio_passthrough_active}", flush=True)
            if prev_detection_enabled != detection_enabled:
                print(f"STATUS:detection_enabled:{detection_enabled}", flush=True)

        # Determine automatic desire (no-op unless thresholds are reached)
        auto_wants_on = None  # None=no change, True=connect, False=disconnect
        if detection_enabled and audio_available:
            if phone_detected:
                idle_counter = 0
                detection_counter += 1
                if detection_counter >= CONSECUTIVE_FRAMES_TRIGGER:
                    auto_wants_on = True
            else:
                detection_counter = 0
                idle_counter += 1
                if idle_counter >= CONSECUTIVE_FRAMES_IDLE:
                    auto_wants_on = False
        else:
            # Detection disabled: reset counters
            detection_counter = 0
            idle_counter = 0

        # Resolve final desired action based on priority rules
        # Priority: manual 'on' forces ON; manual 'off' allows AUTO to turn ON
        desired_action = None  # 'connect' | 'disconnect' | None

        if keybind_enabled and manual_override_state == 'on':
            if not audio_passthrough_active:
                desired_action = 'connect'
        elif keybind_enabled and manual_override_state == 'off':
            if auto_wants_on is True and not audio_passthrough_active:
                desired_action = 'connect'
            elif auto_wants_on is False and audio_passthrough_active:
                desired_action = 'disconnect'
        else:
            # No manual override: follow auto if it requests change
            if auto_wants_on is True and not audio_passthrough_active:
                desired_action = 'connect'
            elif auto_wants_on is False and audio_passthrough_active:
                desired_action = 'disconnect'

        # Execute desired action
        if desired_action == 'connect':
            audio_passthrough_active = True
            connect_audio()
        elif desired_action == 'disconnect':
            audio_passthrough_active = False
            disconnect_audio()
        
        # Small delay to prevent high CPU usage
        # No GUI display - app runs in background
        cv2.waitKey(1)

    # --- Cleanup ---
    # Release video capture
    cap.release()
    print("Application finished.")
    
    # Stop the system tray icon
    if tray_icon:
        tray_icon.stop()

    # Stop background asyncio loop
    try:
        if _bg_loop is not None and _bg_loop.is_running():
            _bg_loop.call_soon_threadsafe(_bg_loop.stop)
    except Exception:
        pass


if __name__ == "__main__":
    main()
