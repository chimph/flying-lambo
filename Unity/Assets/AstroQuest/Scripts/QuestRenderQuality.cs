using UnityEngine;

namespace AstroQuest
{
    static class QuestRenderQuality
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        static void Apply()
        {
            QualitySettings.antiAliasing = 4;
            QualitySettings.globalTextureMipmapLimit = 0;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Report() => Debug.Log($"FLYING LAMBO RENDER: quality {QualitySettings.GetQualityLevel()}, MSAA {QualitySettings.antiAliasing}x, full texture resolution.");
    }
}
