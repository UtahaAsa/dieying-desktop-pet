import sys
import time
import cv2


def clamp(value, low=-1.0, high=1.0):
    return max(low, min(high, value))


def main():
    face_xml = cv2.data.haarcascades + "haarcascade_frontalface_default.xml"
    eye_xml = cv2.data.haarcascades + "haarcascade_eye.xml"
    smile_xml = cv2.data.haarcascades + "haarcascade_smile.xml"
    face_detector = cv2.CascadeClassifier(face_xml)
    eye_detector = cv2.CascadeClassifier(eye_xml)
    smile_detector = cv2.CascadeClassifier(smile_xml)
    camera = cv2.VideoCapture(0, cv2.CAP_DSHOW)
    camera.set(cv2.CAP_PROP_FRAME_WIDTH, 320)
    camera.set(cv2.CAP_PROP_FRAME_HEIGHT, 240)
    if not camera.isOpened():
        print("ERROR|camera", flush=True)
        return 2
    smooth = [0.0, 0.0, 0.0]
    while True:
        ok, frame = camera.read()
        if not ok:
            time.sleep(0.05)
            continue
        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        faces = face_detector.detectMultiScale(gray, 1.15, 5, minSize=(48, 48))
        if len(faces) == 0:
            time.sleep(0.02)
            continue
        x, y, w, h = max(faces, key=lambda item: item[2] * item[3])
        cx = (x + w * 0.5) / float(frame.shape[1])
        cy = (y + h * 0.5) / float(frame.shape[0])
        # Haar 只提供脸框，因此这里用脸框中心作为稳定的头部朝向代理值。
        target = [clamp((cx - 0.5) * 2.2), clamp((cy - 0.48) * 2.0), 0.0]
        for i in range(3):
            smooth[i] = smooth[i] * 0.72 + target[i] * 0.28
        face = gray[y:y + h, x:x + w]
        upper = face[:max(1, int(h * 0.62)), :]
        eyes = eye_detector.detectMultiScale(upper, 1.1, 5, minSize=(max(8, w // 10), max(8, h // 12)))
        lower = face[int(h * 0.42):, :]
        smiles = smile_detector.detectMultiScale(lower, 1.4, 12, minSize=(max(16, w // 4), max(8, h // 12))) if not smile_detector.empty() else []
        expression = 2 if len(smiles) else 0
        if len(eyes) < 2 and w > 70:
            expression = 1
        print("POSE|{:.4f}|{:.4f}|{:.4f}|{}".format(smooth[0], smooth[1], smooth[2], expression), flush=True)
    camera.release()


if __name__ == "__main__":
    sys.exit(main())
