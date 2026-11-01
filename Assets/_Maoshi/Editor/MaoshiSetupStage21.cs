using Maoshi.Audio;
using UnityEditor;
using UnityEngine;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 21: звук и музыка.
    /// - Настройки импорта: эффекты — моно, в памяти распакованы (без задержки); музыка — стерео, Vorbis, потоком с диска.
    /// - На префаб кота: CatAudio (шаги, прыжки, удары) и CatVoice (E — мяу, Q — шипеть, R — «мрр?» по сети).
    /// Менеджер звука (AudioManager) создаётся сам при запуске игры.
    /// Запуск: Maoshi → Setup → Stage 21 - Audio.
    /// </summary>
    public static class MaoshiSetupStage21
    {
        private const string SfxFolder = "Assets/_Maoshi/Resources/Maoshi/Audio/Sfx";
        private const string MusicFolder = "Assets/_Maoshi/Resources/Maoshi/Audio/Music";

        [MenuItem("Maoshi/Setup/Stage 21 - Audio")]
        public static void Setup()
        {
            int sfx = Configure(SfxFolder, false);
            int music = Configure(MusicFolder, true);
            AddToCat();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Maoshi Setup] Этап 21 готов: звуков {sfx}, музыкальных дорожек {music}. Кот: CatAudio + CatVoice (E/Q/R).");
        }

        private static int Configure(string folder, bool music)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return 0;
            int count = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not AudioImporter importer) continue;
                // Музыку «Ночного концерта» настраивает этап 25 (в памяти, без потока — для точного ритма).
                if (path.EndsWith("/concert.wav")) { count++; continue; }
                importer.forceToMono = !music;
                importer.loadInBackground = music;
                var settings = importer.defaultSampleSettings;
                settings.loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = music ? 0.55f : 0.7f;
                settings.preloadAudioData = !music;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
                count++;
            }
            return count;
        }

        private static void AddToCat()
        {
            var root = PrefabUtility.LoadPrefabContents(MaoshiSetupStage2.CatPrefabPath);
            try
            {
                if (root.GetComponent<CatAudio>() == null) root.AddComponent<CatAudio>();
                if (root.GetComponent<CatVoice>() == null) root.AddComponent<CatVoice>();
                PrefabUtility.SaveAsPrefabAsset(root, MaoshiSetupStage2.CatPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
