"""
Download YOLOv8 model for HoldSense
This script downloads and exports the YOLOv8n model to ONNX format.
Run this before building the application if the model is not present.
"""

import os
import sys

try:
    from ultralytics import YOLO
except ImportError:
    print("Error: ultralytics package not found.")
    print("Please install requirements first: pip install -r requirements.txt")
    sys.exit(1)


def download_and_export_model():
    """Download YOLOv8n model and export to ONNX format."""
    onnx_path = "yolov8n.onnx"
    pt_path = "yolov8n.pt"
    
    # Check if ONNX model already exists
    if os.path.exists(onnx_path):
        print(f"✓ {onnx_path} already exists. Skipping download.")
        return True
    
    print("Downloading YOLOv8n model...")
    try:
        # Load model (will download .pt file if not present)
        model = YOLO('yolov8n.pt')
        print("✓ YOLOv8n model downloaded")
        
        # Export to ONNX format
        print("Exporting to ONNX format...")
        model.export(format='onnx')
        
        # Verify ONNX file was created
        if os.path.exists(onnx_path):
            file_size = os.path.getsize(onnx_path) / (1024 * 1024)  # Convert to MB
            print(f"✓ ONNX model exported successfully ({file_size:.2f} MB)")
            
            # Clean up .pt file if it exists (to save space)
            if os.path.exists(pt_path):
                print(f"Cleaning up {pt_path}...")
                os.remove(pt_path)
                print("✓ Cleanup complete")
            
            return True
        else:
            print("✗ Error: ONNX file was not created")
            return False
            
    except Exception as e:
        print(f"✗ Error downloading/exporting model: {e}")
        return False


if __name__ == "__main__":
    print("=" * 50)
    print("HoldSense - YOLOv8 Model Download")
    print("=" * 50)
    
    success = download_and_export_model()
    
    if success:
        print("\n✓ Model is ready! You can now build the application.")
        sys.exit(0)
    else:
        print("\n✗ Failed to download model. Please check your internet connection.")
        sys.exit(1)
