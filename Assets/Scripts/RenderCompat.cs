using UnityEngine;

/// <summary>
/// Выбор шейдера под активный рендер-пайплайн.
///
/// В этом проекте URP установлен как пакет, но не назначен как активный
/// пайплайн (QualitySettings без m_RenderPipeline, в GraphicsSettings
/// m_CustomRenderPipeline = {fileID: 0}). Значит рендерит встроенный
/// пайплайн, а шейдеры URP/Lit под ним не работают — объект рисуется
/// встроенным error-шейдером, то есть розовым/фиолетовым.
///
/// Поэтому шейдер выбирается по фактически активному пайплайну, а не
/// «всегда URP».
/// </summary>
public static class RenderCompat
{
    static Shader cached;
    static bool cachedValid;

    /// <summary>true, если активен URP.</summary>
    public static bool UrpActive
    {
        get
        {
            return UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
        }
    }

    /// <summary>Основной шейдер: Lit для URP, Standard для встроенного пайплайна.</summary>
    public static Shader Surface
    {
        get
        {
            if (cachedValid && cached != null) return cached;
            Shader s = null;
            if (UrpActive)
            {
                s = Shader.Find("Universal Render Pipeline/Lit");
            }
            else
            {
                s = Shader.Find("Standard");
            }
            if (s == null) s = Shader.Find("Sprites/Default");
            if (s == null) s = Shader.Find("UI/Default");
            cached = s;
            cachedValid = true;
            return s;
        }
    }

    /// <summary>Создать материал с подходящим шейдером. Не бросает исключений.</summary>
    public static Material NewSurfaceMaterial()
    {
        Shader s = Surface;
        if (s == null)
        {
            Debug.LogError("[RenderCompat] Не найден ни одного подходящего шейдера.");
            return null;
        }
        return new Material(s);
    }

    /// <summary>Назначить текстуру материалу независимо от пайплайна.</summary>
    public static void ApplyTexture(Material mat, Texture tex, Vector2 tiling)
    {
        if (mat == null || tex == null) return;
        if (mat.HasProperty("_BaseMap"))
        {
            mat.SetTexture("_BaseMap", tex);
            mat.SetTextureScale("_BaseMap", tiling);
        }
        if (mat.HasProperty("_MainTex"))
        {
            mat.SetTexture("_MainTex", tex);
            mat.SetTextureScale("_MainTex", tiling);
        }
    }

    /// <summary>Цвет материала независимо от пайплайна.</summary>
    public static void ApplyColor(Material mat, Color c)
    {
        if (mat == null) return;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        mat.color = c;
    }
}
