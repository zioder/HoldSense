"""
Visual test for YOLOv26 phone detection with debug info.
YOLOv26 uses end-to-end NMS-free inference with output shape (1, 300, 6).
Each detection is [x1, y1, x2, y2, confidence, class_id].
"""
import warnings
import os
os.environ['TF_CPP_MIN_LOG_LEVEL'] = '2'
warnings.filterwarnings('ignore')

import cv2
import numpy as np
import onnxruntime
import time

YOLO_MODEL_ONNX = 'yolo26n.onnx'
CONF_THRESHOLD = 0.20  # Lower default for varied lighting/cameras
PROCESS_EVERY_N_FRAMES = 2

# COCO class names (80 classes)
COCO_NAMES = [
    'person', 'bicycle', 'car', 'motorcycle', 'airplane', 'bus', 'train', 'truck', 'boat', 'traffic light',
    'fire hydrant', 'stop sign', 'parking meter', 'bench', 'bird', 'cat', 'dog', 'horse', 'sheep', 'cow',
    'elephant', 'bear', 'zebra', 'giraffe', 'backpack', 'umbrella', 'handbag', 'tie', 'suitcase', 'frisbee',
    'skis', 'snowboard', 'sports ball', 'kite', 'baseball bat', 'baseball glove', 'skateboard', 'surfboard',
    'tennis racket', 'bottle', 'wine glass', 'cup', 'fork', 'knife', 'spoon', 'bowl', 'banana', 'apple',
    'sandwich', 'orange', 'broccoli', 'carrot', 'hot dog', 'pizza', 'donut', 'cake', 'chair', 'couch',
    'potted plant', 'bed', 'dining table', 'toilet', 'tv', 'laptop', 'mouse', 'remote', 'keyboard',
    'cell phone', 'microwave', 'oven', 'toaster', 'sink', 'refrigerator', 'book', 'clock', 'vase',
    'scissors', 'teddy bear', 'hair drier', 'toothbrush'
]

CELL_PHONE_CLASS = 67
BOX_COLOR_PHONE = (0, 255, 0)   # Green for phones
BOX_COLOR_OTHER = (255, 165, 0)  # Orange for other objects

def preprocess(frame, input_width, input_height):
    """Preprocess frame for YOLOv26 ONNX inference."""
    h, w = frame.shape[:2]
    scale = min(input_width / w, input_height / h)
    new_w, new_h = int(w * scale), int(h * scale)

    resized = cv2.resize(frame, (new_w, new_h), interpolation=cv2.INTER_LINEAR)

    # Pad to model input size with gray (114)
    padded = np.full((input_height, input_width, 3), 114, dtype=np.uint8)
    y_offset = (input_height - new_h) // 2
    x_offset = (input_width - new_w) // 2
    padded[y_offset:y_offset + new_h, x_offset:x_offset + new_w] = resized

    # BGR -> RGB (Ultralytics ONNX expects RGB)
    padded = cv2.cvtColor(padded, cv2.COLOR_BGR2RGB)

    # Normalize and convert to NCHW
    blob = (padded.astype(np.float32) / 255.0).transpose(2, 0, 1)[np.newaxis, ...]
    return blob, scale, x_offset, y_offset


def postprocess_e2e(outputs, conf_threshold, scale, x_offset, y_offset, orig_w, orig_h):
    """
    Process YOLOv26 end-to-end output.
    Output shape: (1, 300, 6) where each row is [x1, y1, x2, y2, confidence, class_id].
    No NMS needed - the model handles it internally.
    """
    raw = np.squeeze(outputs[0])  # (300, 6)
    if raw.ndim != 2 or raw.shape[1] < 6:
        return [], [], 0.0

    confidences = raw[:, 4]
    class_ids = raw[:, 5].astype(int)

    # Filter by confidence
    mask = confidences > conf_threshold
    filtered = raw[mask]
    filtered_confs = confidences[mask]
    filtered_classes = class_ids[mask]

    if len(filtered) == 0:
        # Return max phone score for debugging
        phone_mask = class_ids == CELL_PHONE_CLASS
        max_phone_score = float(np.max(confidences[phone_mask])) if phone_mask.any() else 0.0
        return [], [], max_phone_score

    all_detections = []
    phone_detections = []

    for i in range(len(filtered)):
        x1, y1, x2, y2 = filtered[i, :4]
        conf = float(filtered_confs[i])
        cls_id = int(filtered_classes[i])

        # Scale boxes from model coordinates back to original image
        x1 = (x1 - x_offset) / scale
        y1 = (y1 - y_offset) / scale
        x2 = (x2 - x_offset) / scale
        y2 = (y2 - y_offset) / scale

        # Clip to image bounds
        x1 = max(0, min(x1, orig_w))
        y1 = max(0, min(y1, orig_h))
        x2 = max(0, min(x2, orig_w))
        y2 = max(0, min(y2, orig_h))

        cls_name = COCO_NAMES[cls_id] if cls_id < len(COCO_NAMES) else f'class_{cls_id}'
        det = {
            'box': [int(x1), int(y1), int(x2), int(y2)],
            'score': conf,
            'class_id': cls_id,
            'class_name': cls_name
        }
        all_detections.append(det)
        if cls_id == CELL_PHONE_CLASS:
            phone_detections.append(det)

    # Max phone score for debug display
    phone_mask = class_ids == CELL_PHONE_CLASS
    max_phone_score = float(np.max(confidences[phone_mask])) if phone_mask.any() else 0.0

    return all_detections, phone_detections, max_phone_score


def postprocess_legacy(outputs, conf_threshold, nms_threshold, scale, x_offset, y_offset, orig_w, orig_h):
    """
    Fallback for legacy YOLO output format (1, 84, 8400).
    Used if the model was exported with end2end=False.
    """
    raw = np.squeeze(outputs[0])
    if raw.ndim != 2:
        return [], [], 0.0

    # Handle (84, N) -> transpose to (N, 84)
    if raw.shape[0] in (84, 85) and raw.shape[1] > 85:
        predictions = raw.T
    elif raw.shape[1] in (84, 85):
        predictions = raw
    else:
        predictions = raw if raw.shape[0] > raw.shape[1] else raw.T

    class_offset = 5 if predictions.shape[1] >= 85 else 4
    class_scores = predictions[:, class_offset:]

    best_class = np.argmax(class_scores, axis=1)
    best_score = np.max(class_scores, axis=1)

    mask = best_score > conf_threshold
    filtered = predictions[mask]
    filt_scores = best_score[mask]
    filt_classes = best_class[mask]

    phone_scores = class_scores[:, CELL_PHONE_CLASS] if CELL_PHONE_CLASS < class_scores.shape[1] else np.zeros(len(predictions))
    max_phone_score = float(np.max(phone_scores)) if phone_scores.size else 0.0

    if len(filtered) == 0:
        return [], [], max_phone_score

    all_detections = []
    phone_detections = []

    boxes_xywh = []
    for pred in filtered:
        cx, cy, bw, bh = pred[:4]
        x = cx - bw / 2
        y = cy - bh / 2
        boxes_xywh.append([x, y, bw, bh])
    boxes_xywh = np.array(boxes_xywh, dtype=np.float32)

    # Scale to original image
    boxes_xywh[:, 0] = (boxes_xywh[:, 0] - x_offset) / scale
    boxes_xywh[:, 1] = (boxes_xywh[:, 1] - y_offset) / scale
    boxes_xywh[:, 2] /= scale
    boxes_xywh[:, 3] /= scale

    indices = cv2.dnn.NMSBoxes(boxes_xywh.tolist(), filt_scores.tolist(), conf_threshold, nms_threshold)
    if len(indices) > 0:
        indices = indices.flatten() if hasattr(indices, 'flatten') else indices
        for idx in indices:
            x, y, bw, bh = boxes_xywh[idx]
            cls_id = int(filt_classes[idx])
            cls_name = COCO_NAMES[cls_id] if cls_id < len(COCO_NAMES) else f'class_{cls_id}'
            det = {
                'box': [int(x), int(y), int(x + bw), int(y + bh)],
                'score': float(filt_scores[idx]),
                'class_id': cls_id,
                'class_name': cls_name
            }
            all_detections.append(det)
            if cls_id == CELL_PHONE_CLASS:
                phone_detections.append(det)

    return all_detections, phone_detections, max_phone_score


def main():
    print("Loading YOLOv26 model...")
    if not os.path.exists(YOLO_MODEL_ONNX):
        print(f"ERROR: {YOLO_MODEL_ONNX} not found!")
        return

    # Load model with GPU preference
    providers = ['DmlExecutionProvider', 'CPUExecutionProvider']
    try:
        session = onnxruntime.InferenceSession(YOLO_MODEL_ONNX, providers=providers)
    except Exception as e:
        print(f"DirectML failed ({e}), using CPU provider.")
        session = onnxruntime.InferenceSession(YOLO_MODEL_ONNX, providers=['CPUExecutionProvider'])

    used_providers = session.get_providers()
    print(f"Using provider: {used_providers[0]}")

    input_shape = session.get_inputs()[0].shape
    input_name = session.get_inputs()[0].name
    input_height, input_width = input_shape[2], input_shape[3]
    print(f"Input size: {input_width}x{input_height}")

    # Detect output format
    output_shape = session.get_outputs()[0].shape
    print(f"Output shape: {output_shape}")

    # Check if end-to-end format (1, 300, 6) or legacy (1, 84, 8400)
    is_e2e = (len(output_shape) == 3 and output_shape[2] == 6)
    if is_e2e:
        print("Detected END-TO-END format (NMS-free, one-to-one head)")
        print(f"Max detections per frame: {output_shape[1]}")
    else:
        print("Detected LEGACY format (requires NMS post-processing)")
        print("NOTE: For best YOLOv26 accuracy, re-export with end2end=True")

    # Open webcam
    print("Opening webcam...")
    cap = cv2.VideoCapture(0, cv2.CAP_DSHOW)
    if not cap.isOpened():
        cap = cv2.VideoCapture(0)

    cap.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
    cap.set(cv2.CAP_PROP_FPS, 30)
    cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*'MJPG'))
    cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)

    print("\n=== Controls ===")
    print("Q - Quit")
    print("S - Toggle detection")
    print("+/- - Adjust confidence threshold")
    print("A - Toggle show all objects")
    print("================\n")

    detection_enabled = True
    conf_threshold = CONF_THRESHOLD
    show_all_objects = True  # Show all detected objects, not just phones

    fps_history = []
    frame_count = 0
    cached_all_dets = []
    cached_phone_dets = []
    max_phone_score = 0.0

    while True:
        loop_start = time.time()

        ret, frame = cap.read()
        if not ret:
            continue

        display = frame.copy()
        h, w = frame.shape[:2]

        if detection_enabled:
            frame_count += 1
            if frame_count % PROCESS_EVERY_N_FRAMES == 0:
                blob, scale, x_off, y_off = preprocess(frame, input_width, input_height)
                outputs = session.run(None, {input_name: blob})

                if is_e2e:
                    cached_all_dets, cached_phone_dets, max_phone_score = postprocess_e2e(
                        outputs, conf_threshold, scale, x_off, y_off, w, h
                    )
                else:
                    cached_all_dets, cached_phone_dets, max_phone_score = postprocess_legacy(
                        outputs, conf_threshold, 0.45, scale, x_off, y_off, w, h
                    )

        # Draw detections
        phone_count = len(cached_phone_dets)
        dets_to_draw = cached_all_dets if show_all_objects else cached_phone_dets

        for det in dets_to_draw:
            x1, y1, x2, y2 = det['box']
            score = det['score']
            is_phone = det['class_id'] == CELL_PHONE_CLASS
            color = BOX_COLOR_PHONE if is_phone else BOX_COLOR_OTHER

            cv2.rectangle(display, (x1, y1), (x2, y2), color, 2)

            label = f"{det['class_name']}: {score:.2f}"
            (tw, th), _ = cv2.getTextSize(label, cv2.FONT_HERSHEY_SIMPLEX, 0.6, 2)
            cv2.rectangle(display, (x1, y1 - th - 10), (x1 + tw, y1), color, -1)
            cv2.putText(display, label, (x1, y1 - 5),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 0, 0), 2)

        # Calculate FPS
        loop_time = time.time() - loop_start
        fps = 1.0 / loop_time if loop_time > 0 else 0
        fps_history.append(fps)
        if len(fps_history) > 30:
            fps_history.pop(0)
        avg_fps = sum(fps_history) / len(fps_history)

        # Draw UI overlay
        status = "ON" if detection_enabled else "OFF"
        status_color = (0, 255, 0) if detection_enabled else (0, 0, 255)
        mode = "E2E" if is_e2e else "Legacy"

        cv2.putText(display, f"Detection: {status} ({mode})", (10, 30),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.7, status_color, 2)
        cv2.putText(display, f"Phones: {phone_count}", (10, 60),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2)
        cv2.putText(display, f"FPS: {avg_fps:.1f}", (10, 90),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2)
        cv2.putText(display, f"Conf: {conf_threshold:.2f}", (10, 120),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.6, (200, 200, 200), 1)
        cv2.putText(display, f"MaxPhone: {max_phone_score:.2f}", (10, 145),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.6, (200, 200, 200), 1)
        all_label = "All Objects" if show_all_objects else "Phones Only"
        cv2.putText(display, f"Show: {all_label}", (10, 170),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.6, (200, 200, 200), 1)
        cv2.putText(display, f"Total: {len(cached_all_dets)} dets", (10, 195),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.6, (200, 200, 200), 1)
        cv2.putText(display, "Q:Quit S:Toggle +/-:Conf A:AllObj", (10, h - 10),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.5, (150, 150, 150), 1)

        cv2.imshow("YOLOv26 Phone Detection", display)

        key = cv2.waitKey(1) & 0xFF
        if key == ord('q'):
            break
        elif key == ord('s'):
            detection_enabled = not detection_enabled
        elif key == ord('+') or key == ord('='):
            conf_threshold = min(0.9, conf_threshold + 0.05)
            print(f"Confidence threshold: {conf_threshold:.2f}")
        elif key == ord('-'):
            conf_threshold = max(0.05, conf_threshold - 0.05)
            print(f"Confidence threshold: {conf_threshold:.2f}")
        elif key == ord('a'):
            show_all_objects = not show_all_objects
            print(f"Show all objects: {show_all_objects}")

    cap.release()
    cv2.destroyAllWindows()
    print("Test ended.")

if __name__ == "__main__":
    main()
