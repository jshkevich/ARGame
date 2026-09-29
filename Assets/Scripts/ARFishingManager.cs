using UnityEngine;
using System.Collections;

/// <summary>
/// AR-рыбалка на Vuforia для телефона.
/// Ставит озеро (ARWaterPlacer), удочка от первого лица (FishingRodRig),
/// геймплей: заброс тапом/взмахом → ожидание → поклёвка с вибрацией → вываживание тапами.
/// Работает в Editor (мышь) и на Android/iOS (тач, гироскоп, вибрация).
///
/// Как подключить в сцене:
/// 1. ARCamera от Vuforia (уже есть в 0-Main).
/// 2. Пустой GameObject "FishingRoot" с этим скриптом + ARWaterPlacer + WaterSurface-ребёнок + FishingUI.
/// 3. Моделька удочки — ребёнок ARCamera (скрипт сам найдёт объект "удочка" и привяжет).
/// </summary>
public class ARFishingManager : MonoBehaviour
{
    enum State { NeedPlace, Idle, Casting, Waiting, Bite, Hooked, ShowCatch }

    [Header("Ссылки (могут найтись сами)")]
    public ARWaterPlacer placer;
    public FishingRodRig rod;
    public WaterSurface water;
    public FishingUI ui;
    public Camera arCamera;

    [Header("Настройка клёва")]
    public float minWait = 3f;
    public float maxWait = 8f;
    public float biteWindow = 1.6f;
    [Tooltip("Сколько тапов нужно для вываживания лёгкой рыбы.")]
    public int baseReelTaps = 4;
    [Tooltip("Сколько секунд даётся на вываживание.")]
    public float hookTime = 6f;
    [Tooltip("Длительность полёта поплавка.")]
    public float castDuration = 0.9f;

    [Header("Взмах телефоном")]
    [Tooltip("Порог угловой скорости гироскопа (рад/с) чтобы засчитать взмах как заброс.")]
    public float flickThreshold = 3.5f;
    public float flickCooldown = 1.5f;

    State state = State.NeedPlace;
    float stateTimer;
    float flickCd;
    int score;
    int fishCount;
    int best;
    int tapsNeeded;
    int tapsDone;
    float hookTimer;
    float biteTimer;
    FishSpec currentFish;
    AudioSource audio;

    [System.Serializable]
    struct FishSpec
    {
        public string name;
        public Color color;
        public float length;
        public float minKg;
        public float maxKg;
        public int score;
        public float chance; // вес для рулетки
    }

    FishSpec[] table = new FishSpec[]
    {
        new FishSpec { name="Карась",  color=new Color(0.5f,0.6f,0.3f), length=0.35f, minKg=0.2f, maxKg=0.8f, score=10, chance=40f },
        new FishSpec { name="Окунь",   color=new Color(0.9f,0.5f,0.2f), length=0.4f,  minKg=0.3f, maxKg=1.2f, score=20, chance=30f },
        new FishSpec { name="Щука",    color=new Color(0.3f,0.55f,0.3f), length=0.6f, minKg=1.0f, maxKg=3.0f, score=50, chance=20f },
        new FishSpec { name="Золотая", color=new Color(1f,0.8f,0.1f),   length=0.32f, minKg=0.3f, maxKg=0.6f, score=100, chance=10f },
    };

    void Awake()
    {
        if (arCamera == null) arCamera = Camera.main;
        if (arCamera == null) arCamera = FindAnyObjectByType<Camera>();
        if (placer == null) placer = FindAnyObjectByType<ARWaterPlacer>();
        if (rod == null) rod = FindAnyObjectByType<FishingRodRig>();
        if (water == null) water = FindAnyObjectByType<WaterSurface>();
        if (ui == null) ui = FindAnyObjectByType<FishingUI>();

        // Если скрипты раскиданы — создадим недостающее на этом же объекте
        if (placer == null) placer = gameObject.AddComponent<ARWaterPlacer>();
        if (rod == null) rod = gameObject.AddComponent<FishingRodRig>();
        if (ui == null) ui = gameObject.AddComponent<FishingUI>();
        if (water == null)
        {
            GameObject wgo = new GameObject("Water");
            wgo.transform.SetParent(transform, false);
            water = wgo.AddComponent<WaterSurface>();
            placer.waterRoot = wgo.transform;
        }
        if (placer.waterRoot == null && water != null)
            placer.waterRoot = water.transform;
        if (placer.arCamera == null)
            placer.arCamera = arCamera;

        audio = gameObject.AddComponent<AudioSource>();
        audio.playOnAwake = false;

        // Телефон: не гасить экран, сенсоры вкл (New Input System)
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        ARInput.EnableSensors();

        best = PlayerPrefs.GetInt("ARFish_Best", 0);
        score = 0; fishCount = 0;

        Application.targetFrameRate = 60;
    }

    void Start()
    {
        rod.AttachToCamera(arCamera);
        rod.HideBobber();
        ui.OnReplace(() => placer.ResetPlacement());
        ToNeedPlace();
    }

    void Update()
    {
        if (flickCd > 0f) flickCd -= Time.deltaTime;
        UpdateFlick();

        switch (state)
        {
            case State.NeedPlace:
                if (placer.IsPlaced) ToIdle();
                else
                {
                    ui.SetHint("Наведи камеру на пол и тапни — появится озеро");
                    ui.SetAction("", null);
                }
                break;

            case State.Idle:
                if (!placer.IsPlaced) { ToNeedPlace(); break; }
                ui.SetHint("Тапни по воде или нажми ЗАБРОС / взмахни телефоном");
                ui.SetScore(score, fishCount, best);
                // Тап по воде = точный заброс в точку
                if (ScreenTapped(out Vector2 sp) && !FishingUI.IsPointerOverUI())
                    TryCast(sp);
                break;

            case State.Casting:
                break;

            case State.Waiting:
                stateTimer -= Time.deltaTime;
                ui.SetHint($"Ждём... ({Mathf.Ceil(stateTimer)}с) Не шуми!");
                // Досрочная подсечка = срыв (учим не спамить)
                if (ScreenTapped(out _) && !FishingUI.IsPointerOverUI())
                {
                    ui.ShowCenter("Рано! Рыбу спугнул...", Color.gray, 1.2f);
                    PlayTone(200, 0.2f);
                    rod.ReelIn();
                    ToIdleDelayed(1.2f);
                }
                else if (stateTimer <= 0f)
                {
                    ToBite();
                }
                break;

            case State.Bite:
                biteTimer -= Time.deltaTime;
                ui.SetHint($"КЛЮЁТ! Жми ТАЩИ! ({biteTimer:F1}с)");
                if (ScreenTapped(out _) && !FishingUI.IsPointerOverUI())
                {
                    ToHooked(); // тап по экрану тоже подсекает
                }
                else if (biteTimer <= 0f)
                {
                    Missed();
                }
                break;

            case State.Hooked:
                hookTimer -= Time.deltaTime;
                // Тапы + тряска телефоном мотают
                if (ScreenTapped(out _) && !FishingUI.IsPointerOverUI())
                    ReelTap();
                if (ShakeDetected())
                    ReelTap();
                // Натяжение медленно падает — надо долбить
                float tension = (float)tapsDone / Mathf.Max(1, tapsNeeded);
                ui.SetTension(tension);
                ui.SetHint($"Тапай чтобы вытащить! {tapsDone}/{tapsNeeded}  ({hookTimer:F0}с)");
                if (tapsDone >= tapsNeeded)
                    Caught();
                else if (hookTimer <= 0f)
                    Missed();
                break;

            case State.ShowCatch:
                break;
        }
    }

    // ---------- Переходы ----------

    void ToNeedPlace()
    {
        state = State.NeedPlace;
        ui.ShowTension(false);
        ui.SetScore(score, fishCount, best);
    }

    void ToIdle()
    {
        state = State.Idle;
        ui.ShowTension(false);
        ui.SetScore(score, fishCount, best);
        ui.SetHint("Тапни по воде или нажми ЗАБРОС");
        ui.SetAction("🎣 ЗАБРОСИТЬ", () => TryCast(null), new Color(0.15f, 0.6f, 0.3f, 0.95f));
    }

    void ToIdleDelayed(float delay)
    {
        state = State.Casting; // блокируем ввод на время
        StartCoroutine(IdleAfter(delay));
    }
    IEnumerator IdleAfter(float delay) { yield return new WaitForSeconds(delay); ToIdle(); }

    void TryCast(Vector2? screenPoint)
    {
        if (state != State.Idle) return;
        state = State.Casting;
        ui.SetAction("", null);
        ui.SetHint("Заброс...");
        PlayTone(600, 0.15f);

        Vector3 target = water.RandomPointOnWater();
        // Точный заброс: луч из тапа в воду
        if (screenPoint.HasValue && arCamera != null)
        {
            Ray ray = arCamera.ScreenPointToRay(screenPoint.Value);
            // Плоскость воды
            Plane plane = new Plane(Vector3.up, water.transform.position);
            if (plane.Raycast(ray, out float dist))
            {
                Vector3 hit = ray.GetPoint(dist);
                // Проверяем что попали в радиус озера
                float maxR = water.size * 0.45f * water.transform.lossyScale.x;
                if (Vector3.Distance(hit, water.transform.position) < maxR)
                    target = hit + Vector3.up * 0.05f;
            }
        }

        rod.CastTo(target, castDuration, () =>
        {
            water.Ripple(target, 0.4f);
            PlayTone(400, 0.1f);
            state = State.Waiting;
            stateTimer = Random.Range(minWait, maxWait);
        });
    }

    void ToBite()
    {
        state = State.Bite;
        biteTimer = biteWindow;
        currentFish = RollFish();
        rod.BiteJerk();
        water.Ripple(rod.bobber.position, 0.6f);
        ui.FlashBite(biteWindow);
        ui.SetAction("❗ ТАЩИ!", () => ToHooked(), new Color(0.9f, 0.2f, 0.2f, 0.95f));
        PlayTone(880, 0.3f);
#if !UNITY_EDITOR
        Handheld.Vibrate();
#endif
    }

    void ToHooked()
    {
        if (state != State.Bite) return;
        state = State.Hooked;
        // Чем тяжелее рыба — тем больше тапов
        float w = (currentFish.minKg + currentFish.maxKg) * 0.5f;
        tapsNeeded = baseReelTaps + Mathf.RoundToInt(w * 2f);
        tapsDone = 0;
        hookTimer = hookTime;
        ui.ShowTension(true);
        ui.SetTension(0f);
        ui.SetAction("💪 ТЯНИ! (тапай)", () => ReelTap(), new Color(1f, 0.55f, 0.1f, 0.95f));
        PlayTone(700, 0.15f);
        StartCoroutine(rod.RodShake(hookTime, 0.02f));
    }

    void ReelTap()
    {
        if (state != State.Hooked) return;
        tapsDone++;
        PlayTone(500 + tapsDone * 40, 0.07f);
        StartCoroutine(rod.RodShake(0.2f, 0.03f));
        // Поплавок дёргается к камере
        if (rod.bobber != null)
            rod.bobber.position += Vector3.up * 0.05f;
    }

    void Caught()
    {
        state = State.ShowCatch;
        ui.ShowTension(false);
        float weight = Random.Range(currentFish.minKg, currentFish.maxKg);
        score += currentFish.score;
        fishCount++;
        if (score > best) { best = score; PlayerPrefs.SetInt("ARFish_Best", best); }
        ui.SetScore(score, fishCount, best);
        ui.SetAction("", null);
        ui.ShowCenter($"{currentFish.name} {weight:F1} кг! +{currentFish.score}", Color.green, 2.5f);
        PlayCatchSound();
#if !UNITY_EDITOR
        Handheld.Vibrate();
#endif
        // Рыбка летит от поплавка к камере
        Vector3 from = rod.bobber.position;
        var fish = FishView.BuildFish(currentFish.name, currentFish.color, currentFish.length, weight, currentFish.score);
        fish.transform.position = from;
        fish.transform.localScale = Vector3.one * water.transform.lossyScale.x;
        StartCoroutine(FishFlight(fish.transform, from, 2.2f));
        rod.ReelIn();
        water.Ripple(from, 0.7f);
        StartCoroutine(IdleAfter(2.6f));
    }

    IEnumerator FishFlight(Transform fish, Vector3 from, float duration)
    {
        float t = 0f;
        while (t < 1f && fish != null)
        {
            t += Time.deltaTime / duration;
            float k = Mathf.Clamp01(t);
            Vector3 camPos = arCamera.transform.position;
            Vector3 camFwd = arCamera.transform.forward;
            Vector3 to = camPos + camFwd * 0.7f + Vector3.down * 0.15f;
            Vector3 p = Vector3.Lerp(from, to, k);
            p.y += Mathf.Sin(k * Mathf.PI) * 0.4f;
            fish.position = p;
            fish.Rotate(Vector3.up, 360f * Time.deltaTime);
            yield return null;
        }
        yield return new WaitForSeconds(0.8f);
        if (fish != null) Destroy(fish.gameObject);
    }

    void Missed()
    {
        state = State.ShowCatch;
        ui.ShowTension(false);
        ui.SetAction("", null);
        ui.ShowCenter("Сорвалась... Попробуй ещё!", new Color(1f, 0.4f, 0.4f), 1.5f);
        PlayTone(180, 0.35f);
        rod.ReelIn();
        StartCoroutine(IdleAfter(1.6f));
    }

    // ---------- Ввод: тап / взмах / тряска ----------

    bool ScreenTapped(out Vector2 pos)
    {
        return ARInput.TapBegan(out pos);
    }

    void UpdateFlick()
    {
        if (flickCd > 0f || state != State.Idle) return;
        if (!ARInput.HasGyro()) return;
        Vector3 r = ARInput.GyroRate(); // рад/с
        if (r.magnitude > flickThreshold)
        {
            flickCd = flickCooldown;
            TryCast(null); // взмах = заброс в случайную точку
        }
    }

    Vector3 lastAccel;
    float shakeCd;
    bool ShakeDetected()
    {
        if (shakeCd > 0f) { shakeCd -= Time.deltaTime; return false; }
        Vector3 a = ARInput.Acceleration();
        float d = (a - lastAccel).magnitude;
        lastAccel = a;
        if (d > 0.6f)
        {
            shakeCd = 0.25f;
            return true;
        }
        return false;
    }

    // ---------- Звук без ассетов ----------

    void PlayTone(float freq, float duration)
    {
        if (audio == null) return;
        audio.PlayOneShot(MakeTone(freq, duration));
    }

    void PlayCatchSound()
    {
        StartCoroutine(CatchJingle());
    }
    IEnumerator CatchJingle()
    {
        PlayTone(523, 0.12f); yield return new WaitForSeconds(0.13f);
        PlayTone(659, 0.12f); yield return new WaitForSeconds(0.13f);
        PlayTone(784, 0.25f);
    }

    AudioClip MakeTone(float freq, float duration)
    {
        int rate = 22050;
        int n = Mathf.RoundToInt(rate * duration);
        float[] data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float env = 1f - (i / (float)n); // затухание
            data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * 0.5f * env;
        }
        AudioClip clip = AudioClip.Create($"tone_{freq}", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    FishSpec RollFish()
    {
        float total = 0f;
        foreach (var f in table) total += f.chance;
        float r = Random.Range(0f, total);
        foreach (var f in table)
        {
            r -= f.chance;
            if (r <= 0f) return f;
        }
        return table[0];
    }
}
