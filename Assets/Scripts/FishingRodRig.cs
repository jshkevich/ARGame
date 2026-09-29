using UnityEngine;
using System.Collections;

/// <summary>
/// Удочка от первого лица: держит модельку, леску (LineRenderer) и поплавок.
/// Моделька «удочка.fbx» должна быть ребёнком AR-камеры (справа внизу).
/// Tip/Line/Bobber создаются кодом, если не назначены — ничего настраивать не надо.
/// </summary>
public class FishingRodRig : MonoBehaviour
{
    [Header("Модель")]
    [Tooltip("Корень модельки удочки. Если пусто — найдём по имени 'удочка' среди детей камеры.")]
    public Transform rodRoot;
    [Tooltip("Кончик удилища. Если пусто — создадим автоматически по bounds модели.")]
    public Transform tipPoint;
    [Tooltip("Смещение кончика если автоопределение промахнулось (в локальных координатах удочки).")]
    public Vector3 tipOffset = Vector3.zero;

    [Header("Леска и поплавок")]
    public LineRenderer line;
    public Transform bobber;

    [Header("Леска и крючок из модели")]
    [Tooltip("Анимировать леску и крючок, которые уже есть в модели (меши «Леска» и «Крючок»), вместо служебных.")]
    public bool useModelLineAndHook = true;
    [Tooltip("Растягивать меш лески модели от кончика к крючку. По умолчанию выключено — леска остаётся в натуральной позе модели. Включай, только если леска в модели не дотягивается до крючка.")]
    public bool stretchModelLine = true;
    [Tooltip("Меш лески внутри модели. Если пусто — найдём по имени «Леска».")]
    public Transform modelLine;
    [Tooltip("Меш крючка внутри модели. Если пусто — найдём по имени «Крючок». Он же играет роль поплавка.")]
    public Transform modelHook;
    [Tooltip("Смещение крючка вдоль лески, чтобы он не утопал в воде.")]
    public Vector3 hookWaterOffset = Vector3.zero;
    [Tooltip("Толщина лески в мировых метрах. Меш лески в модели тонкий (около 0.4 мм в мире), поэтому задаём явно, иначе её не видно.")]
    public float lineWorldThickness = 0.006f;

    public float lineSag = 0.25f;

    [Header("Вид от первого лица")]
    [Tooltip("Позиция удочки относительно камеры. X=0 — строго по центру кадра.")]
    public Vector3 rodLocalPos = new Vector3(0f, -1f, 0.85f);
    [Tooltip("Поворот удочки относительно камеры.")]
    public Vector3 rodLocalEuler = new Vector3(0f, 180f, 0f);
    [Tooltip("Масштаб удочки. Модель из FBX приходит с неудобным размером, поэтому задаём вручную.")]
    public Vector3 rodLocalScale = new Vector3(0.08f, 0.08f, 0.08f);
    [Tooltip("Если true — размер считается автоматически по targetRodLength и rodLocalScale игнорируется.")]
    public bool autoFitRodSize = false;
    [Tooltip("Если true — позиция удочки зажимается внутрь кадра. По умолчанию выключено, чтобы уважать заданную позицию.")]
    public bool limitRodToView = false;
    [Tooltip("Видимая длина удочки, если включён autoFitRodSize.")]
    public float targetRodLength = 0.95f;
    public float idleSwayAmount = 0.02f;

    Camera ownerCam;
    Quaternion rodBaseRot;
    Vector3 rodBasePos;
    Coroutine castRoutine;
    bool bobberFloating;
    float floatPhase;
    int lineAxis = 1;
    float modelLineOriginalLength;
    Vector3 modelLineBaseScale = Vector3.one;
    Vector3 modelLineLocalSize = Vector3.one;
    bool loggedParts;

    void Awake()
    {
        ownerCam = GetComponentInParent<Camera>();
        if (ownerCam == null) ownerCam = Camera.main;
        EnsureRod();
        EnsureLine();
        EnsureBobber();
    }

    void Start()
    {
        if (rodRoot != null)
        {
            rodBasePos = rodRoot.localPosition;
            rodBaseRot = rodRoot.localRotation;
        }
        // Сенсоры (гироскоп/акселерометр) через New Input System
        ARInput.EnableSensors();
    }

    void Update()
    {
        if (rodRoot != null)
        {
            // Лёгкое покачивание + реакция на гироскоп
            float sway = Mathf.Sin(Time.time * 1.4f) * idleSwayAmount;
            Vector3 gyroTilt = Vector3.zero;
            if (ARInput.HasGyro())
            {
                var rot = ARInput.GyroRate(); // рад/с
                gyroTilt = new Vector3(
                    Mathf.Clamp(-rot.x * 0.02f, -0.08f, 0.08f),
                    Mathf.Clamp(-rot.y * 0.02f, -0.08f, 0.08f), 0f);
            }
            rodRoot.localPosition = rodBasePos + new Vector3(sway * 0.3f, sway, 0f) + gyroTilt * 0.5f;
        }

        UpdateLine();

        if (bobberFloating && bobber != null)
        {
            floatPhase += Time.deltaTime * 3f;
            bobber.position += Vector3.up * Mathf.Sin(floatPhase) * 0.0015f;
            bobber.Rotate(Vector3.up, 20f * Time.deltaTime);
        }
    }

    void OnValidate()
    {
        // Чтобы переворот по X было видно в эдиторе сразу, без запуска Play.
        if (rodRoot != null && rodRoot.parent != null && rodRoot.parent.GetComponent<Camera>() != null)
        {
            rodRoot.localPosition = rodLocalPos;
            rodRoot.localRotation = Quaternion.Euler(rodLocalEuler);
        }
    }

    public void AttachToCamera(Camera cam)
    {
        ownerCam = cam;
        if (rodRoot == null) EnsureRod();
        FitRodToView(); // подгоняем позицию под FOV/аспект телефона до привязки
        rodRoot.SetParent(cam.transform, false);
        rodRoot.localPosition = rodLocalPos;
        rodRoot.localRotation = Quaternion.Euler(rodLocalEuler);
        ApplyRodSize();
        rodBasePos = rodRoot.localPosition;
        rodBaseRot = rodRoot.localRotation;
    }

    /// <summary>
    /// Ограничиваем локальную позицию frustum'ом камеры, удерживая удочку
    /// в видимой нижней части кадра по центру.
    /// </summary>
    void FitRodToView()
    {
        // По умолчанию выключено: значения rodLocalPos задаёт автор сцены,
        // и клампить их нельзя — иначе указанная позиция (например y = -1)
        // молча превращалась в значение у края кадра.
        if (!limitRodToView || ownerCam == null) return;

        float aspect = ownerCam.aspect;
        if (aspect <= 0.01f) aspect = 9f / 19f; // типичный портретный телефон
        float vFov = ownerCam.fieldOfView * Mathf.Deg2Rad;
        if (vFov <= 0.01f) vFov = 60f * Mathf.Deg2Rad;
        float hFov = 2f * Mathf.Atan(Mathf.Tan(vFov * 0.5f) * aspect);
        float z = Mathf.Max(0.3f, rodLocalPos.z);

        float maxX = Mathf.Tan(hFov * 0.5f) * z * 0.60f;
        float maxY = Mathf.Tan(vFov * 0.5f) * z * 0.60f;

        rodLocalPos.x = Mathf.Clamp(rodLocalPos.x, -maxX, maxX);
        rodLocalPos.y = Mathf.Clamp(rodLocalPos.y, -maxY, -0.08f);
    }

    /// <summary>
    /// Размер удочки. По умолчанию берём явный rodLocalScale — модель из FBX
    /// приходит со своим масштабом, и подгонять его автоматически не нужно.
    /// Автоподбор остаётся на случай, если включён autoFitRodSize.
    /// </summary>
    void ApplyRodSize()
    {
        if (rodRoot == null) return;
        if (!autoFitRodSize)
        {
            rodRoot.localScale = rodLocalScale;
            return;
        }
        NormalizeRodSize();
    }

    /// <summary>
    /// Автоматический подбор масштаба по длине удилища. Леска, крючок и
    /// катушка из расчёта исключены — иначе длина завышалась.
    /// </summary>
    void NormalizeRodSize()
    {
        if (rodRoot == null) return;
        Renderer shaft = RodShaftRenderer();
        if (shaft == null) return;

        float worldLen = shaft.bounds.size.magnitude;
        if (worldLen < 0.0001f) return;

        float worldScale = rodRoot.lossyScale.x;
        if (worldScale < 0.0001f) worldScale = 1f;
        float localLen = worldLen / worldScale;
        if (localLen < 0.0001f) return;

        rodRoot.localScale = Vector3.one * (targetRodLength / localLen);
    }

    void EnsureRod()
    {
        if (rodRoot != null)
        {
            CleanInternalSensors(rodRoot);
            return;
        }

        // 1. Ищем модельку fishrod / удочка среди объектов
        Transform found = null;
        var all = FindObjectsByType<Transform>(FindObjectsInactive.Exclude);
        foreach (var t in all)
        {
            string n = t.name.ToLower();
            if (n.Contains("fishrod") || n.Contains("удочка") || n.Contains("rod"))
            {
                found = t;
                if (found.GetComponent<MeshRenderer>() != null && found.parent != null)
                    found = found.parent;
                break;
            }
        }
        if (found != null)
        {
            rodRoot = found;
        }
        else
        {
            // 2. Заглушка если модель не найдена
            GameObject stub = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stub.name = "RodStub";
            Destroy(stub.GetComponent<Collider>());
            stub.transform.localScale = new Vector3(0.02f, 0.7f, 0.02f);
            rodRoot = stub.transform;
        }

        CleanInternalSensors(rodRoot);

        Camera cam = ownerCam != null ? ownerCam : Camera.main;
        if (cam != null && rodRoot.parent != cam.transform)
        {
            rodRoot.SetParent(cam.transform, false);
            rodRoot.localPosition = rodLocalPos;
            rodRoot.localRotation = Quaternion.Euler(rodLocalEuler);
            ApplyRodSize();
        }

        // Авто-tip: берём меш самого удилища («Палка»). Раньше бралась верхняя
        // точка AABB всей модели, а туда попадала ещё и леска с крючком —
        // из-за чего леска тянулась не из кончика удилища.
        if (tipPoint == null)
        {
            GameObject tip = new GameObject("RodTip");
            tip.transform.SetParent(rodRoot, false);
            Renderer r = RodShaftRenderer();
            if (r != null)
            {
                Vector3 localTop = rodRoot.InverseTransformPoint(
                    r.bounds.center + Vector3.up * r.bounds.extents.y);
                tip.transform.localPosition = localTop + tipOffset;
            }
            else
            {
                tip.transform.localPosition = new Vector3(0f, 0.7f, 0f) + tipOffset;
            }
            tipPoint = tip.transform;
        }
    }

    /// <summary>
    /// Рендерер именно удилища, без лески, крючка и катушки.
    /// </summary>
    Renderer RodShaftRenderer()
    {
        if (rodRoot == null) return null;
        Renderer best = null;
        float bestLen = 0f;
        foreach (var r in rodRoot.GetComponentsInChildren<Renderer>())
        {
            if (r == null) continue;
            if (r.transform == modelLine || r.transform == modelHook) continue;
            if (modelLine != null && r.transform.IsChildOf(modelLine)) continue;
            if (modelHook != null && r.transform.IsChildOf(modelHook)) continue;
            string n = r.transform.name.ToLower();
            if (n.Contains("леска") || n.Contains("крючок") || n.Contains("leska") || n.Contains("hook"))
                continue;

            float len = r.bounds.size.magnitude;
            if (len > bestLen) { bestLen = len; best = r; }
        }
        return best;
    }

    /// <summary>
    /// Находит меши «Леска» и «Крючок» внутри модели удочки и запоминает
    /// геометрию лески, чтобы потом растягивать её от кончика удилища к крючку.
    /// </summary>
    void EnsureModelParts()
    {
        if (!useModelLineAndHook || rodRoot == null) return;

        if (modelLine == null)
        {
            Transform t = FindChildByName(rodRoot, new[] { "леска", "leska", "line" });
            if (t != null) modelLine = t;
        }
        if (modelHook == null)
        {
            Transform t = FindChildByName(rodRoot, new[] { "крючок", "hook", "крюк" });
            if (t != null) modelHook = t;
        }

        // Геометрия лески: какая локальная ось длинная и какова её длина
        if (modelLine != null && modelLineOriginalLength <= 0.0001f)
        {
            var r = modelLine.GetComponent<Renderer>();
            if (r != null)
            {
                Vector3 local = modelLine.InverseTransformVector(r.bounds.size);
                if (Mathf.Abs(local.x) > Mathf.Abs(local.y) && Mathf.Abs(local.x) > Mathf.Abs(local.z))
                    lineAxis = 0;
                else if (Mathf.Abs(local.z) > Mathf.Abs(local.y))
                    lineAxis = 2;
                else
                    lineAxis = 1;
                modelLineOriginalLength = Mathf.Max(0.0001f, local[lineAxis]);
                modelLineBaseScale = modelLine.localScale;
                modelLineLocalSize = local;
            }
        }

        if (!loggedParts)
        {
            loggedParts = true;
            Debug.Log($"[FishingRodRig] rodRoot={(rodRoot != null ? rodRoot.name : "NULL")} " +
                      $"line={(modelLine != null ? modelLine.name : "NOT FOUND")} " +
                      $"hook={(modelHook != null ? modelHook.name : "NOT FOUND")} " +
                      $"lineAxis={lineAxis} lineLen={modelLineOriginalLength:F3}");
        }
    }

    Transform FindChildByName(Transform root, string[] keys)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == root) continue;
            string n = t.name.ToLower();
            for (int i = 0; i < keys.Length; i++)
                if (n.Contains(keys[i])) return t;
        }
        return null;
    }

    void CleanInternalSensors(Transform root)
    {
        // Отключаем лишние всплывающие камеры/источники света из FBX
        foreach (var c in root.GetComponentsInChildren<Camera>(true))
            c.enabled = false;
        foreach (var l in root.GetComponentsInChildren<Light>(true))
            l.enabled = false;
    }

    void EnsureLine()
    {
        if (line != null) return;
        GameObject go = new GameObject("FishingLine");
        go.transform.SetParent(transform, false);
        line = go.AddComponent<LineRenderer>();
        line.positionCount = 12;
        line.startWidth = 0.008f;
        line.endWidth = 0.008f;
        line.material = RenderCompat.NewSurfaceMaterial();
        RenderCompat.ApplyColor(line.material, Color.white);
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.enabled = false;
    }

    void EnsureBobber()
    {
        // Если в модели есть крючок — он и играет роль поплавка/грузила.
        // Вся существующая логика заброса, поклёвки и вываживания работает
        // с полем bobber, поэтому менять её не пришлось.
        if (bobber == null && useModelLineAndHook)
        {
            EnsureModelParts();
            if (modelHook != null)
            {
                bobber = modelHook;
                // Крючок всегда виден — он же кончик лески
            }
        }

        if (bobber != null) return;
        GameObject b = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        b.name = "Bobber";
        Destroy(b.GetComponent<Collider>());
        b.transform.localScale = Vector3.one * 0.07f;
        // Красно-белый поплавок: два материала не сделать на сфере — красим в красный, видно хорошо
        var rend = b.GetComponent<Renderer>();
        Material m = RenderCompat.NewSurfaceMaterial();
        RenderCompat.ApplyColor(m, Color.red);
        if (m != null) rend.material = m;
        // Белая полоска — маленький ребёнок-сфера
        GameObject stripe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        stripe.name = "Stripe";
        Destroy(stripe.GetComponent<Collider>());
        stripe.transform.SetParent(b.transform, false);
        stripe.transform.localScale = new Vector3(1.02f, 0.4f, 1.02f);
        var sr = stripe.GetComponent<Renderer>();
        Material wm = RenderCompat.NewSurfaceMaterial();
        RenderCompat.ApplyColor(wm, Color.white);
        if (wm != null) sr.material = wm;

        bobber = b.transform;
    }

    void UpdateLine()
    {
        if (tipPoint == null || bobber == null) return;

        bool hasModelLine = useModelLineAndHook && modelLine != null && modelLineOriginalLength > 0.0001f;

        // Леска и крючок всегда видны
        if (hasModelLine && !modelLine.gameObject.activeSelf)
            modelLine.gameObject.SetActive(true);
        if (bobber != null && !bobber.gameObject.activeSelf)
            bobber.gameObject.SetActive(true);

        Vector3 a = TipPos();
        Vector3 b = bobber.position + hookWaterOffset;

                // Основной путь — леска из самой модели.
        // По умолчанию НЕ растягиваем её: у модели есть своя натуральная
        // поза лески, и перестановка меша каждый кадр ломала её.
        if (hasModelLine)
        {
            if (stretchModelLine) StretchModelLine(a, b);
            // LineRenderer выключаем, чтобы не было двух лесок
            if (line != null) line.enabled = false;
            return;
        }

        // Запасной путь — обычный LineRenderer
        if (line == null || !line.enabled) return;
        for (int i = 0; i < line.positionCount; i++)
        {
            float t = i / (float)(line.positionCount - 1);
            Vector3 p = Vector3.Lerp(a, b, t);
            p.y -= Mathf.Sin(t * Mathf.PI) * lineSag * Vector3.Distance(a, b) * 0.15f;
            line.SetPosition(i, p);
        }
    }

    /// <summary>
    /// Растягивает меш лески модели так, чтобы он шёл от кончика удилища
    /// к крючку: позиция — середина, поворот — вдоль направления,
    /// масштаб — по длине с сохранением толщины.
    /// </summary>
    void StretchModelLine(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        float len = d.magnitude;
        if (len < 0.0001f) return;

        // Позиция всегда в мировых координатах
        Vector3 mid = (from + to) * 0.5f;
        mid.y -= Mathf.Min(0.12f, len * lineSag * 0.25f); // лёгкий провис
        modelLine.position = mid;

        Transform parent = modelLine.parent;

        // Поворот и длину считаем в локальных осях родителя: у удочки
        // масштаб 0.08, и без этой поправки леска вышла бы в 12 раз короче.
        Vector3 localDir = parent != null
            ? parent.InverseTransformDirection(d / len)
            : d / len;
        float localLen = (parent != null ? parent.InverseTransformVector(d) : d).magnitude;

        if (parent != null) modelLine.localRotation = Quaternion.FromToRotation(AxisDir(), localDir);
        else modelLine.rotation = Quaternion.FromToRotation(AxisDir(), d / len);

        // Масштаб: по длине — чтобы леска дотянулась до крючка, а поперёк —
        // явно по толщине в мировых метрах. Иначе при масштабе удочки 0.08
        // меш сечения около 0.4 мм, и леску просто не видно.
        Vector3 s = modelLineBaseScale;
        s[lineAxis] = Mathf.Abs(s[lineAxis]) * (localLen / modelLineOriginalLength);

        float parentScale = parent != null ? Mathf.Abs(parent.lossyScale.x) : 1f;
        if (parentScale < 0.0001f) parentScale = 1f;
        for (int i = 0; i < 3; i++)
        {
            if (i == lineAxis) continue;
            if (modelLineLocalSize[i] > 0.000001f)
                s[i] = lineWorldThickness / (modelLineLocalSize[i] * parentScale);
        }
        modelLine.localScale = s;
    }

    Vector3 AxisDir()
    {
        return lineAxis == 0 ? Vector3.right : (lineAxis == 2 ? Vector3.forward : Vector3.up);
    }

    Vector3 TipPos() { return tipPoint != null ? tipPoint.position : transform.position + Vector3.forward; }

    /// <summary>Заброс поплавка по параболе к цели.</summary>
    public void CastTo(Vector3 target, float duration, System.Action onLanded)
    {
        if (castRoutine != null) StopCoroutine(castRoutine);
        castRoutine = StartCoroutine(CastRoutine(target, duration, onLanded));
        // Взмах удочкой
        StopAllCoroutines2();
        StartCoroutine(RodFlick());
    }

    IEnumerator CastRoutine(Vector3 target, float duration, System.Action onLanded)
    {
        bobber.gameObject.SetActive(true);
        line.enabled = true;
        bobberFloating = false;
        Vector3 start = TipPos();
        float t = 0f;
        float height = Vector3.Distance(start, target) * 0.35f + 0.5f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.1f, duration);
            float k = Mathf.Clamp01(t);
            Vector3 p = Vector3.Lerp(start, target, k);
            p.y += Mathf.Sin(k * Mathf.PI) * height;
            // Не даём уйти под воду раньше времени
            float waterY = target.y;
            if (k < 1f) p.y = Mathf.Max(p.y, waterY + 0.05f);
            bobber.position = p;
            yield return null;
        }
        bobber.position = target;
        bobberFloating = true;
        floatPhase = 0f;
        onLanded?.Invoke();
    }

    /// <summary>Смотать поплавок обратно к кончику.</summary>
    public void ReelIn(float duration = 0.4f)
    {
        if (castRoutine != null) StopCoroutine(castRoutine);
        StartCoroutine(ReelRoutine(duration));
    }

    IEnumerator ReelRoutine(float duration)
    {
        bobberFloating = false;
        Vector3 start = bobber.position;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.05f, duration);
            bobber.position = Vector3.Lerp(start, TipPos(), Mathf.Clamp01(t));
            yield return null;
        }
        bobber.gameObject.SetActive(false);
        line.enabled = false;
    }

    /// <summary>Дёргание поплавка при поклёвке — видно и в AR.</summary>
    public void BiteJerk()
    {
        if (bobber != null && bobber.gameObject.activeSelf)
            StartCoroutine(JerkRoutine());
        StartCoroutine(RodShake(0.5f, 0.03f));
    }

    IEnumerator JerkRoutine()
    {
        Vector3 base0 = bobber.position;
        for (int i = 0; i < 6; i++)
        {
            bobber.position = base0 + Vector3.up * (i % 2 == 0 ? -0.06f : 0.03f);
            yield return new WaitForSeconds(0.09f);
        }
        bobber.position = base0;
    }

    IEnumerator RodFlick()
    {
        if (rodRoot == null) yield break;
        Quaternion from = rodBaseRot;
        Quaternion up = rodBaseRot * Quaternion.Euler(-35f, 0f, 0f);
        float t = 0f;
        while (t < 0.18f) { t += Time.deltaTime; rodRoot.localRotation = Quaternion.Slerp(from, up, t / 0.18f); yield return null; }
        t = 0f;
        while (t < 0.3f) { t += Time.deltaTime; rodRoot.localRotation = Quaternion.Slerp(up, from, t / 0.3f); yield return null; }
        rodRoot.localRotation = from;
    }

    public IEnumerator RodShake(float duration, float amp)
    {
        if (rodRoot == null) yield break;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            rodRoot.localRotation = rodBaseRot * Quaternion.Euler(
                Mathf.Sin(t * 40f) * amp * 30f, 0f, Mathf.Cos(t * 33f) * amp * 30f);
            yield return null;
        }
        rodRoot.localRotation = rodBaseRot;
    }

    public void HideBobber()
    {
        bobberFloating = false;
        if (bobber != null) bobber.gameObject.SetActive(false);
        if (line != null) line.enabled = false;
    }

    // Чтобы RodFlick не убивал CastRoutine — раздельные стопы
    void StopAllCoroutines2() { /* намеренно пусто: flick идёт параллельно */ }
}
