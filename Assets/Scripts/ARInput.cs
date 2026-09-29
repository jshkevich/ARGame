using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;

/// <summary>
/// Ввод ТОЛЬКО через New Input System. Никаких обращений к UnityEngine.Input
/// в горячих путях — иначе при activeInputHandler = Input System летит
/// InvalidOperationException каждый кадр.
/// </summary>
public static class ARInput
{
    public static void EnableSensors()
    {
        try
        {
            if (!EnhancedTouchSupport.enabled)
                EnhancedTouchSupport.Enable();
        }
        catch { /* ignore */ }

        try
        {
            if (UnityEngine.InputSystem.Gyroscope.current != null && !UnityEngine.InputSystem.Gyroscope.current.enabled)
                InputSystem.EnableDevice(UnityEngine.InputSystem.Gyroscope.current);
            if (UnityEngine.InputSystem.Accelerometer.current != null && !UnityEngine.InputSystem.Accelerometer.current.enabled)
                InputSystem.EnableDevice(UnityEngine.InputSystem.Accelerometer.current);
        }
        catch { /* ignore */ }
    }

    /// <summary>Тап/клик начался в этом кадре. Мышь (Editor) + тач (телефон).</summary>
    public static bool TapBegan(out Vector2 pos)
    {
        pos = Vector2.zero;

        try
        {
            var ts = Touchscreen.current;
            if (ts != null)
            {
                foreach (var t in ts.touches)
                {
                    try
                    {
                        if (t.press.wasPressedThisFrame)
                        {
                            pos = t.position.ReadValue();
                            return true;
                        }
                    }
                    catch { }
                }
                try
                {
                    if (ts.primaryTouch.press.wasPressedThisFrame)
                    {
                        pos = ts.primaryTouch.position.ReadValue();
                        return true;
                    }
                }
                catch { }
            }
        }
        catch { }

        try
        {
            var m = Mouse.current;
            if (m != null && m.leftButton.wasPressedThisFrame)
            {
                pos = m.position.ReadValue();
                return true;
            }
        }
        catch { }
        return false;
    }

    public static Vector2 PointerPosition()
    {
        try
        {
            var ts = Touchscreen.current;
            if (ts != null)
            {
                foreach (var t in ts.touches)
                {
                    try { if (t.press.isPressed) return t.position.ReadValue(); }
                    catch { }
                }
                try
                {
                    if (ts.primaryTouch.press.isPressed)
                        return ts.primaryTouch.position.ReadValue();
                }
                catch { }
            }
            var m = Mouse.current;
            if (m != null) return m.position.ReadValue();
        }
        catch { }
        return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
    }

    public static int PressedTouchCount()
    {
        try
        {
            var ts = Touchscreen.current;
            if (ts == null) return 0;
            int n = 0;
            foreach (var t in ts.touches)
            {
                try { if (t.press.isPressed) n++; }
                catch { }
            }
            return n;
        }
        catch { return 0; }
    }

    public static bool TwoFingerTapBegan()
    {
        try
        {
            var ts = Touchscreen.current;
            if (ts == null) return false;
            int pressed = 0;
            bool began = false;
            foreach (var t in ts.touches)
            {
                try
                {
                    if (t.press.isPressed) pressed++;
                    if (t.press.wasPressedThisFrame) began = true;
                }
                catch { }
            }
            return pressed >= 2 && began;
        }
        catch { return false; }
    }

    /// <summary>Угловая скорость гироскопа, рад/с. Vector3.zero если нет датчика.</summary>
    public static Vector3 GyroRate()
    {
        try
        {
            var g = UnityEngine.InputSystem.Gyroscope.current;
            if (g != null && g.enabled)
                return g.angularVelocity.ReadValue();
        }
        catch { }
        return Vector3.zero;
    }

    public static bool HasGyro()
    {
        try { return UnityEngine.InputSystem.Gyroscope.current != null; }
        catch { return false; }
    }

    public static Vector3 Acceleration()
    {
        try
        {
            var a = UnityEngine.InputSystem.Accelerometer.current;
            if (a != null && a.enabled)
                return a.acceleration.ReadValue();
        }
        catch { }
        return Vector3.zero;
    }

    /// <summary>Тап по UI? Через RaycastAll, без старого Input.</summary>
    public static bool IsPointerOverUI()
    {
        if (EventSystem.current == null) return false;
        try
        {
            // Без активного касания/клика — точно не над UI (защита от ложных забросов)
            bool hasPointer = false;
            try
            {
                var ts = Touchscreen.current;
                if (ts != null)
                {
                    foreach (var t in ts.touches)
                    {
                        try { if (t.press.isPressed) { hasPointer = true; break; } }
                        catch { }
                    }
                }
                var m = Mouse.current;
                // Мышь в Editor есть всегда — проверяем её позицию тоже
                if (!hasPointer && m != null)
                {
                    try
                    {
                        if (m.leftButton.isPressed || m.rightButton.isPressed || m.middleButton.isPressed)
                            hasPointer = true;
                        else
                        {
                            // В Editor hover над кнопкой тоже считается UI — проверяем всегда,
                            // на телефоне Mouse.current обычно null, так что безопасно
#if UNITY_EDITOR || UNITY_STANDALONE
                            hasPointer = true;
#endif
                        }
                    }
                    catch { }
                }
            }
            catch { }
            if (!hasPointer) return false;

            Vector2 screenPos = PointerPosition();
            var data = new PointerEventData(EventSystem.current) { position = screenPos };
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(data, results);
            return results.Count > 0;
        }
        catch
        {
            return false;
        }
    }
}
