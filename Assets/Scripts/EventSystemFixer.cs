using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Убивает StandaloneInputModule ДО первого EventSystem.Update.
/// Именно он спамит InvalidOperationException при activeInputHandler = Input System,
/// т.к. внутри читает старый UnityEngine.Input.mousePosition.
/// Выполняется раньше всех (order -10000) + дублирующий RuntimeInitialize.
/// Повесь никуда не надо — работает сам через RuntimeInitializeOnLoadMethod.
/// </summary>
[DefaultExecutionOrder(-10000)]
public class EventSystemFixer : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void FixBeforeScene()
    {
        // На этом этапе сцены ещё нет — ставим хук на загрузку сцены
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) => FixAll();
    }

    void Awake()
    {
        FixAll();
    }

    public static void FixAll()
    {
        var systems = FindObjectsByType<EventSystem>(FindObjectsInactive.Include);
        foreach (var es in systems)
        {
            if (es == null) continue;
            var oldModule = es.GetComponent<StandaloneInputModule>();
            if (oldModule != null)
            {
                Object.Destroy(oldModule);
            }
            if (es.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>() == null)
            {
                es.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }
        }
    }
}
