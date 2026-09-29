using UnityEngine;

/// <summary>
/// Размещение AR-озера для рыбалки с Vuforia.
/// Логика без жёсткой зависимости от API Vuforia (через рефлексию),
/// поэтому скрипт компилируется даже без пакета, а в рантайме подхватывает трекинг.
/// Приоритет:
/// 1. Если в сцене есть ImageTarget / ObserverBehaviour в статусе TRACKED — озеро становится его ребёнком.
/// 2. Иначе — тап по центру экрана ставит озеро в 2 метрах перед AR-камерой (Ground-режим).
/// Работает и в Editor (мышь), и на телефоне (тач).
/// </summary>
public class ARWaterPlacer : MonoBehaviour
{
    [Header("Ссылки")]
    [Tooltip("Корень озера (плоскость воды + тени рыб). Если пусто — возьмём дочерний WaterSurface.")]
    public Transform waterRoot;

    [Tooltip("AR-камера (Vuforia ARCamera). Если пусто — Camera.main.")]
    public Camera arCamera;

    [Header("Ground-режим (без маркера)")]
    [Tooltip("Дистанция выставления озера перед камерой при тапе (fallback, если луч не попал в пол).")]
    public float placeDistance = 1.8f;
    [Tooltip("Смещение озера вниз относительно камеры (камера ~1.4м, вода на полу).")]
    public float groundOffsetY = -1.0f;
    [Tooltip("Размер озера.")]
    public float waterSize = 2.0f;
    [Tooltip("Можно ли переставить озеро повторным тапом двумя пальцами / кнопкой.")]
    public bool allowReplace = true;

    [Header("Позиция на экране телефона")]
    [Tooltip("Где по вертикали должен появиться центр озера (0=низ, 0.5=центр). 0.38-0.42 = чуть ниже центра, удобно для ловли.")]
    [Range(0.15f, 0.6f)]
    public float targetScreenY = 0.40f;
    [Tooltip("Мин/макс дистанция до озера, чтобы не спавнилось под ногами или вдали.")]
    public float minDistance = 1.2f;
    public float maxDistance = 4.0f;

    [Header("Маркер-режим (Vuforia ImageTarget)")]
    [Tooltip("Если true — каждый кадр ищем затрэканный ImageTarget и цепляем озеро к нему.")]
    public bool attachToImageTarget = true;
    [Tooltip("Масштаб озера когда оно на маркере.")]
    public float markerWaterScale = 1.0f;

    public bool IsPlaced { get; private set; }
    public bool IsOnMarker { get; private set; }

    Transform originalParent;
    Vector3 originalScale;

    void Awake()
    {
        if (arCamera == null) arCamera = Camera.main;
        if (waterRoot == null)
        {
            var ws = GetComponentInChildren<WaterSurface>();
            if (ws != null) waterRoot = ws.transform;
            else waterRoot = transform;
        }
        originalParent = waterRoot.parent;
        originalScale = waterRoot.localScale;
    }

    void Start()
    {
        if (waterRoot != null)
            waterRoot.gameObject.SetActive(false);
        IsPlaced = false;
    }

    void Update()
    {
        if (arCamera == null) arCamera = Camera.main;
        if (arCamera == null || waterRoot == null) return;

        // 1. Пробуем прилипнуть к маркеру Vuforia (если он трекается).
        if (attachToImageTarget)
        {
            Transform tracked = FindTrackedObserver();
            if (tracked != null)
            {
                if (!IsOnMarker || waterRoot.parent != tracked)
                {
                    waterRoot.SetParent(tracked, false);
                    waterRoot.localPosition = Vector3.zero;
                    waterRoot.localRotation = Quaternion.identity;
                    waterRoot.localScale = Vector3.one * markerWaterScale;
                    waterRoot.gameObject.SetActive(true);
                }
                IsPlaced = true;
                IsOnMarker = true;
                return;
            }
            else if (IsOnMarker)
            {
                // Маркер потеряли — открепляем, оставляем озеро в мировых координатах
                // (чтобы поплавок не пропал резко), либо прячем если ground запрещён.
                // Оставляем на месте: запоминаем world transform и открепляем.
                waterRoot.SetParent(originalParent, true);
                IsOnMarker = false;
                // IsPlaced остаётся true — можно продолжать ловить.
            }
        }

        // 2. Ground-режим: первый тап (не по UI) ставит озеро перед камерой.
        if (!IsPlaced)
        {
            if (TouchOnEmptyArea(out Vector2 tap))
                PlaceInFrontOfCamera(tap);
        }
        else if (allowReplace && !IsOnMarker)
        {
            // Перестановка: два пальца одновременно или долгий тап? Упростим: два пальца = переставить.
            if (TwoFingerTap())
                PlaceInFrontOfCamera(null);
        }
    }

    public void PlaceInFrontOfCamera() => PlaceInFrontOfCamera(null);

    public void PlaceInFrontOfCamera(Vector2? preferredScreenPoint)
    {
        if (arCamera == null || waterRoot == null) return;
        waterRoot.SetParent(originalParent, true);

        float groundY = arCamera.transform.position.y + groundOffsetY;
        Vector3 pos;

        // Ставим озеро туда, где луч из нужной точки экрана (чуть ниже центра)
        // пересекает плоскость пола. Тогда на любом телефоне озеро появляется
        // в комфортной зоне кадра, а не в самом низу.
        // Если пользователь тапнул по полу — уважаем его тап, но не даём уйти
        // в самый низ экрана: нижние 25% подтягиваем к targetScreenY.
        Vector2 screenPt = new Vector2(Screen.width * 0.5f, Screen.height * targetScreenY);
        if (preferredScreenPoint.HasValue)
        {
            Vector2 tap = preferredScreenPoint.Value;
            float minY = Screen.height * 0.30f; // ниже — зона кнопки/края, туда озеро не ставим
            float clampedY = Mathf.Max(tap.y, minY);
            // Верхнюю половину тоже мягко тянем к центру чтобы озеро не улетело вдаль
            float maxY = Screen.height * 0.75f;
            clampedY = Mathf.Min(clampedY, maxY);
            screenPt = new Vector2(tap.x, clampedY);
        }
        Ray ray = arCamera.ScreenPointToRay(screenPt);
        Plane ground = new Plane(Vector3.up, new Vector3(0f, groundY, 0f));
        if (ray.direction.y < -0.03f && ground.Raycast(ray, out float dist))
        {
            Vector3 hit = ray.GetPoint(dist);
            // Ограничиваем дистанцию: не под ногами и не вдали
            Vector3 flat = hit - arCamera.transform.position;
            flat.y = 0f;
            float d = flat.magnitude;
            if (d < minDistance || d > maxDistance)
            {
                Vector3 dir = d > 0.001f ? flat.normalized : ForwardFlat();
                float cd = Mathf.Clamp(d, minDistance, maxDistance);
                hit = arCamera.transform.position + dir * cd;
                hit.y = groundY;
            }
            pos = hit;
        }
        else
        {
            // Fallback: камера смотрит в горизонт/вверх — кладём на фиксированной
            // дистанции с лёгким наклоном вниз (~12°), чтобы озеро было видно.
            Vector3 fwd = ForwardFlat();
            pos = arCamera.transform.position + fwd * placeDistance;
            pos.y = groundY;
        }

        waterRoot.position = pos;
        waterRoot.rotation = Quaternion.identity;
        waterRoot.localScale = Vector3.one * waterSize;
        waterRoot.gameObject.SetActive(true);

        IsPlaced = true;
        IsOnMarker = false;
    }

    Vector3 ForwardFlat()
    {
        Vector3 fwd = arCamera.transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
        return fwd.normalized;
    }

    public void ResetPlacement()
    {
        IsPlaced = false;
        IsOnMarker = false;
        if (waterRoot != null)
            waterRoot.gameObject.SetActive(false);
    }

    // Тап одним пальцем / клик мышью, при условии что палец не над UI-кнопкой.
    // Проверку "над UI" делает FishingUI (блокирует raycast), здесь — грубая проверка:
    // нижние 25% экрана отданы кнопке, там озеро не ставим.
    bool TouchOnEmptyArea(out Vector2 tapPos)
    {
        tapPos = Vector2.zero;
        if (ARInput.TapBegan(out Vector2 p))
        {
            if (p.y < Screen.height * 0.25f) return false; // зона кнопки
            tapPos = p;
            return true;
        }
        return false;
    }

    bool TwoFingerTap()
    {
        return ARInput.TwoFingerTapBegan();
    }

    /// <summary>
    /// Ищем любой Vuforia ObserverBehaviour в статусе TRACKED через рефлексию.
    /// Не требует using Vuforia — работает по имени типа.
    /// </summary>
    Transform FindTrackedObserver()
    {
        var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude);
        foreach (var b in behaviours)
        {
            if (b == null) continue;
            string tn = b.GetType().FullName;
            if (tn == null) continue;
            // Vuforia: ObserverBehaviour, ImageTargetBehaviour
            if (!tn.Contains("ObserverBehaviour") && !tn.Contains("ImageTargetBehaviour"))
                continue;
            try
            {
                var prop = b.GetType().GetProperty("TargetStatus");
                if (prop != null)
                {
                    object status = prop.GetValue(b, null);
                    if (status != null)
                    {
                        string s = status.ToString(); // e.g. "TRACKED", "EXTENDED_TRACKED", "NO_POSE"...
                        if (s.Contains("TRACKED"))
                            return b.transform;
                    }
                }
            }
            catch { /* ignore */ }
        }
        return null;
    }
}
