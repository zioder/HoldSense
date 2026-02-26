"""
Visual test for YOLOv26 phone detection.
Shows webcam feed with bounding boxes around detected phones.
Press 'Q' to quit, 'S' to toggle detection on/off.
"""
import warnings
import os
os.environ['TF_CPP_MIN_LOG_LEVEL'] = '2'
warnings.filterwarnings('ignore')

import cv2
import numpy as np
import onnxruntime

# YOLO Model Configuration
YOLO_MODEL_ONNX = 'yolo26n.onnx'
CONF_THRESHOLD = 0.45

# Colors for drawing
BOX_COLOR = (0, 255, 0)  # Green
TEXT_COLOR = (0, 255, 0)

def main():
    print("Loading YOLOv26 model...")
    if not os.path.exists(YOLO_MODEL_ONNX):
        print(f"ERROR: {YOLO_MODEL_ONNX} not found! Run: python download_model.py")
        return

    # Load model
    try:
        session = onnxruntime.InferenceSession(
            YOLO_MODEL_ONNX, 
            providers=['DmlExecutionProvider', 'CPUExecutionProvider']
        )
        print("Model loaded with GPU acceleration.")
    except Exception as e:
        print(f"GPU failed ({e}), using CPU.")
        session = onnxruntime.InferenceSession(
            YOLO_MODEL_ONNX, 
            providers=['CPUExecutionProvider']
        )
        print("Model loaded with CPU.")

    # Get input dimensions
    model_inputs = session.get_inputs()
    input_shape = model_inputs[0].shape
    input_name = model_inputs[0].name
    input_height, input_width = input_shape[2], input_shape[3]
    print(f"Model input size: {input_width}x{input_height}")

    # Open webcam
    print("Opening webcam...")
    cap = cv2.VideoCapture(0, cv2.CAP_DSHOW)
    if not cap.isOpened():
        cap = cv2.VideoCapture(0)
    if not cap.isOpened():
        print("ERROR: Could not open webcam!")
        return

    print("\n=== Visual Test Mode ===")
    print("Show your phone to the camera")
    print("Press 'Q' to quit")
    print("Press 'S' to toggle detection")
    print("========================\n")

    detection_enabled = True
    frame_count = 0
    fps_start = cv2.getTickCount()

    while True:
        ret, frame = cap.read()
        if not ret:
            continue

        display_frame = frame.copy()
        phones_detected = 0

        if detection_enabled:
            # Preprocess
            h, w = frame.shape[:2]
            scale = min(input_width / w, input_height / h)
            scaled_w, scaled_h = int(w * scale), int(h * scale)
            scaled = cv2.resize(frame, (scaled_w, scaled_h), interpolation=cv2.INTER_AREA)

            top_pad = (input_height - scaled_h) // 2
            left_pad = (input_width - scaled_w) // 2
            padded = cv2.copyMakeBorder(
                scaled, top_pad, input_height - scaled_h - top_pad,
                left_pad, input_width - scaled_w - left_pad,
                cv2.BORDER_CONSTANT, value=(114, 114, 114)
            )

            blob = cv2.dnn.blobFromImage(padded, 1/255.0, (input_width, input_height), swapRB=True, crop=False)

            # Run inference
            outputs = session.run(None, {input_name: blob})

            # Post-process
            if outputs:
                output_tensor = np.squeeze(outputs[0])
                if len(output_tensor.shape) == 2 and output_tensor.shape[0] < output_tensor.shape[1]:
                    rows = output_tensor.T
                else:
                    rows = output_tensor

                boxes = []
                confidences = []

                for row in rows:
                    phone_conf = row[4 + 67] if row.shape[0] >= 84 else 0
                    if phone_conf > CONF_THRESHOLD:
                        cx, cy, w_box, h_box = row[:4]
                        x1 = cx - w_box/2
                        y1 = cy - h_box/2
                        boxes.append([x1, y1, w_box, h_box])
                        confidences.append(float(phone_conf))

                # Apply NMS and draw
                if boxes:
                    indices = cv2.dnn.NMSBoxes(boxes, confidences, CONF_THRESHOLD, 0.5)
                    if len(indices) > 0:
                        indices = indices.flatten() if hasattr(indices, 'flatten') else indices
                        phones_detected = len(indices)
                        for i in indices:
                            x, y, wb, hb = boxes[i]
                            conf = confidences[i]

                            # Scale back to original frame
                            x = (x - left_pad) / scale
                            y = (y - top_pad) / scale
                            wb = wb / scale
                            hb = hb / scale

                            x1, y1 = int(x), int(y)
                            x2, y2 = int(x + wb), int(y + hb)

                            # Draw box
                            cv2.rectangle(display_frame, (x1, y1), (x2, y2), BOX_COLOR, 2)
                            label = f"Phone: {conf:.2f}"
                            (tw, th), _ = cv2.getTextSize(label, cv2.FONT_HERSHEY_SIMPLEX, 0.6, 2)
                            cv2.rectangle(display_frame, (x1, y1-th-10), (x1+tw, y1), BOX_COLOR, -1)
                            cv2.putText(display_frame, label, (x1, y1-5),
                                       cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 0, 0), 2)

        # Calculate FPS
        frame_count += 1
        fps_end = cv2.getTickCount()
        fps = frame_count / ((fps_end - fps_start) / cv2.getTickFrequency())
        if frame_count % 30 == 0:
            fps_start = fps_end
            frame_count = 0

        # Draw UI overlay
        status_text = "DETECTION: ON" if detection_enabled else "DETECTION: OFF"
        status_color = (0, 255, 0) if detection_enabled else (0, 0, 255)
        cv2.putText(display_frame, status_text, (10, 30),
                   cv2.FONT_HERSHEY_SIMPLEX, 0.7, status_color, 2)
        cv2.putText(display_frame, f"Phones: {phones_detected}", (10, 60),
                   cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2)
        cv2.putText(display_frame, f"FPS: {fps:.1f}", (10, 90),
                   cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2)
        cv2.putText(display_frame, "Press Q to quit, S to toggle", (10, 120),
                   cv2.FONT_HERSHEY_SIMPLEX, 0.5, (200, 200, 200), 1)

        # Show frame
        cv2.imshow("YOLOv26 Phone Detection Test", display_frame)

        key = cv2.waitKey(1) & 0xFF
        if key == ord('q') or key == ord('Q'):
            break
        elif key == ord('s') or key == ord('S'):
            detection_enabled = not detection_enabled
            print(f"Detection: {'ON' if detection_enabled else 'OFF'}")

    cap.release()
    cv2.destroyAllWindows()
    print("Visual test ended.")

if __name__ == "__main__":
    main()
