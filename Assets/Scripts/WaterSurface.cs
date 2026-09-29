using UnityEngine;

/// <summary>
/// Процедурное AR-озеро: анимированная вода + тени рыб под водой + круги от поплавка.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class WaterSurface : MonoBehaviour
{
    [Header("Вода")]
    public int segments = 24;
    public float size = 1f; // итоговый масштаб задаёт ARWaterPlacer, здесь базовый 1x1
    public float waveHeight = 0.03f;
    public float waveSpeed = 1.6f;
    public Color waterColor = new Color(0.15f, 0.45f, 0.75f, 0.85f);
    public Texture2D waterTexture;
    public Vector2 textureTiling = new Vector2(3f, 3f);
    public Vector2 textureScrollSpeed = new Vector2(0.03f, 0.02f);

    [Header("Рыбки-тени")]
    public int shadowFishCount = 5;

    Mesh mesh;
    Vector3[] baseVerts;
    Vector3[] workVerts;
    Transform[] shadowFish;
    float[] shadowSpeed;
    float[] shadowRadius;
    float[] shadowAngle;
    Material waterMat;
    bool loggedMaterial;

    GameObject ripplePrefabRef;

    void Awake()
    {
        FindWaterTextureIfMissing();
        BuildWater();
        BuildShadowFish();
    }

    void FindWaterTextureIfMissing()
    {
        if (waterTexture != null) return;
        var allTex = Resources.FindObjectsOfTypeAll<Texture2D>();
        foreach (var t in allTex)
        {
            if (t != null && t.name.ToLower().Contains("water"))
            {
                waterTexture = t;
                break;
            }
        }
    }

    void Update()
    {
        AnimateWaves();
        AnimateShadowFish();
        AnimateTexture();
    }

    void AnimateTexture()
    {
        if (waterMat == null || waterTexture == null) return;
        Vector2 offset = textureScrollSpeed * Time.time;
        if (waterMat.HasProperty("_BaseMap"))
            waterMat.SetTextureOffset("_BaseMap", offset);
        if (waterMat.HasProperty("_MainTex"))
            waterMat.SetTextureOffset("_MainTex", offset);
    }

    void BuildWater()
    {
        var mf = GetComponent<MeshFilter>();
        var mr = GetComponent<MeshRenderer>();

        mesh = new Mesh();
        mesh.name = "ARWater";

        int n = segments + 1;
        baseVerts = new Vector3[n * n];
        Vector2[] uv = new Vector2[n * n];
        int[] tris = new int[segments * segments * 6];

        for (int z = 0; z < n; z++)
        {
            for (int x = 0; x < n; x++)
            {
                float px = (x / (float)segments - 0.5f) * size;
                float pz = (z / (float)segments - 0.5f) * size;
                baseVerts[z * n + x] = new Vector3(px, 0f, pz);
                uv[z * n + x] = new Vector2(x / (float)segments, z / (float)segments);
            }
        }

        int t = 0;
        for (int z = 0; z < segments; z++)
        {
            for (int x = 0; x < segments; x++)
            {
                int i = z * n + x;
                tris[t++] = i;
                tris[t++] = i + n;
                tris[t++] = i + 1;
                tris[t++] = i + 1;
                tris[t++] = i + n;
                tris[t++] = i + n + 1;
            }
        }

        mesh.vertices = baseVerts;
        mesh.uv = uv;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mf.mesh = mesh;

        workVerts = new Vector3[baseVerts.Length];
        System.Array.Copy(baseVerts, workVerts, baseVerts.Length);

        // Шейдер выбирается под активный пайплайн (URP в проекте не назначен,
        // рендерит встроенный — URP/Lit давал розовую воду).
        Material mat = RenderCompat.NewSurfaceMaterial();
        if (mat == null) return;
        waterMat = mat;

        RenderCompat.ApplyColor(mat, Color.white);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.6f);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.6f);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.02f);

        // Текстура: в URP она живёт в _BaseMap, во встроенном — в _MainTex
        RenderCompat.ApplyTexture(mat, waterTexture, textureTiling);

        mr.material = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        if (!loggedMaterial)
        {
            loggedMaterial = true;
            Debug.Log($"[WaterSurface] urp={RenderCompat.UrpActive} " +
                      $"shader='{mat.shader.name}' " +
                      $"texture={(waterTexture != null ? waterTexture.name : "NULL")}");
        }

        // Невидимая опора-дно под водой чтобы было на что ловить лучом (опционально)
        var col = GetComponent<MeshCollider>();
        if (col == null) gameObject.AddComponent<MeshCollider>();
    }

    void AnimateWaves()
    {
        if (mesh == null) return;
        float time = Time.time * waveSpeed;
        for (int i = 0; i < baseVerts.Length; i++)
        {
            Vector3 v = baseVerts[i];
            v.y = Mathf.Sin(time + v.x * 6f) * waveHeight * 0.5f
                + Mathf.Cos(time * 1.3f + v.z * 7f) * waveHeight * 0.5f;
            workVerts[i] = v;
        }
        mesh.vertices = workVerts;
        mesh.RecalculateNormals();
    }

    void BuildShadowFish()
    {
        shadowFish = new Transform[shadowFishCount];
        shadowSpeed = new float[shadowFishCount];
        shadowRadius = new float[shadowFishCount];
        shadowAngle = new float[shadowFishCount];

        for (int i = 0; i < shadowFishCount; i++)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "ShadowFish_" + i;
            go.transform.SetParent(transform, false);
            Destroy(go.GetComponent<Collider>());
            // Приплюснутая тёмная тень
            go.transform.localScale = new Vector3(0.12f, 0.02f, 0.05f);
            var r = go.GetComponent<Renderer>();
            Material m = RenderCompat.NewSurfaceMaterial();
            RenderCompat.ApplyColor(m, new Color(0.05f, 0.1f, 0.2f));
            r.material = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            shadowFish[i] = go.transform;
            shadowSpeed[i] = Random.Range(0.2f, 0.6f) * (Random.value > 0.5f ? 1f : -1f);
            shadowRadius[i] = Random.Range(0.15f, 0.42f);
            shadowAngle[i] = Random.Range(0f, Mathf.PI * 2f);
        }
    }

    void AnimateShadowFish()
    {
        if (shadowFish == null) return;
        foreach (var f in shadowFish) { if (f == null) return; }
        for (int i = 0; i < shadowFish.Length; i++)
        {
            shadowAngle[i] += shadowSpeed[i] * Time.deltaTime;
            float x = Mathf.Cos(shadowAngle[i]) * shadowRadius[i];
            float z = Mathf.Sin(shadowAngle[i]) * shadowRadius[i];
            shadowFish[i].localPosition = new Vector3(x, -0.05f, z);
            // Разворот по движению
            float dir = shadowSpeed[i] > 0 ? 1f : -1f;
            shadowFish[i].localRotation = Quaternion.Euler(0f, -shadowAngle[i] * Mathf.Rad2Deg * dir + (dir > 0 ? 90f : -90f), 0f);
        }
    }

    /// <summary>Круги на воде в точке поплавка. Вызывать при приводнении и поклёвке.</summary>
    public void Ripple(Vector3 worldPos, float maxSize = 0.5f)
    {
        StartCoroutine(RippleRoutine(worldPos, maxSize));
    }

    System.Collections.IEnumerator RippleRoutine(Vector3 worldPos, float maxSize)
    {
        // Кольцо без PrimitiveType.Torus (его нет в старых Unity) — плоский диск-волна.
        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "Ripple";
        Destroy(ring.GetComponent<Collider>());
        ring.transform.position = worldPos + Vector3.up * 0.02f;
        ring.transform.localScale = new Vector3(0.05f, 0.005f, 0.05f);
        var rend = ring.GetComponent<Renderer>();
        Material m = RenderCompat.NewSurfaceMaterial();
        RenderCompat.ApplyColor(m, Color.white);
        rend.material = m;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime * 1.5f;
            float s = Mathf.Lerp(0.05f, maxSize, t);
            ring.transform.localScale = new Vector3(s, 0.005f, s);
            Color c = m.color;
            c.a = 0.8f * (1f - t);
            m.color = c;
            yield return null;
        }
        Destroy(ring);
    }

    /// <summary>Точка на воде под камерой (куда полетит поплавок) — случайная в радиусе озера.</summary>
    public Vector3 RandomPointOnWater()
    {
        float r = size * 0.35f * transform.lossyScale.x;
        Vector2 p = Random.insideUnitCircle * r;
        Vector3 wp = transform.position + new Vector3(p.x, 0.05f, p.y);
        return wp;
    }
}
