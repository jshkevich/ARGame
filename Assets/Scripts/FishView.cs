using UnityEngine;

/// <summary>
/// Процедурная рыбка без ассетов: тело + хвост + глаз.
/// Цвет и размер задаются менеджером по типу рыбы.
/// После поимки летит к камере и показывает вес.
/// </summary>
public class FishView : MonoBehaviour
{
    public string fishName = "Окунь";
    public float weightKg;
    public int score;

    Transform tail;
    float swimPhase;

    public static FishView BuildFish(string fishName, Color color, float length, float weightKg, int score)
    {
        GameObject root = new GameObject("Fish_" + fishName);
        var view = root.AddComponent<FishView>();
        view.fishName = fishName;
        view.weightKg = weightKg;
        view.score = score;

        Shader sh = RenderCompat.Surface;

        // Тело
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localScale = new Vector3(length, length * 0.45f, length * 0.35f);
        Destroy(body.GetComponent<Collider>());
        var br = body.GetComponent<Renderer>();
        Material bm = new Material(sh);
        bm.color = color;
        br.material = bm;

        // Хвост — сплюснутый конус (куб повёрнутый)
        GameObject tail = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tail.name = "Tail";
        tail.transform.SetParent(root.transform, false);
        tail.transform.localPosition = new Vector3(-length * 0.55f, 0f, 0f);
        tail.transform.localScale = new Vector3(length * 0.15f, length * 0.4f, 0.02f);
        tail.transform.localRotation = Quaternion.Euler(0f, 0f, 20f);
        Destroy(tail.GetComponent<Collider>());
        var tr = tail.GetComponent<Renderer>();
        Material tm = new Material(sh);
        tm.color = color * 0.85f;
        tr.material = tm;
        view.tail = tail.transform;

        // Плавник сверху
        GameObject fin = GameObject.CreatePrimitive(PrimitiveType.Cube);
        fin.name = "Fin";
        fin.transform.SetParent(root.transform, false);
        fin.transform.localPosition = new Vector3(0f, length * 0.28f, 0f);
        fin.transform.localScale = new Vector3(length * 0.3f, length * 0.18f, 0.02f);
        Destroy(fin.GetComponent<Collider>());
        fin.GetComponent<Renderer>().material = tm;

        // Глаза
        for (int s = -1; s <= 1; s += 2)
        {
            GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eye.name = "Eye";
            eye.transform.SetParent(root.transform, false);
            eye.transform.localPosition = new Vector3(length * 0.32f, length * 0.08f, s * length * 0.16f);
            eye.transform.localScale = Vector3.one * length * 0.12f;
            Destroy(eye.GetComponent<Collider>());
            Material em = new Material(sh);
            em.color = Color.black;
            eye.GetComponent<Renderer>().material = em;
        }
        return view;
    }

    void Update()
    {
        swimPhase += Time.deltaTime * 8f;
        if (tail != null)
            tail.localRotation = Quaternion.Euler(0f, Mathf.Sin(swimPhase) * 30f, 20f);
        // Лёгкое виляние всем телом
        transform.Rotate(Vector3.up, Mathf.Sin(swimPhase) * 0.3f * Time.deltaTime * 10f * 0.1f);
    }
}
