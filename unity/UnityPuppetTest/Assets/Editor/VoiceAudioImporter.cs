using UnityEditor;
using UnityEngine;

public sealed class VoiceAudioImporter : AssetPostprocessor
{
    private void OnPreprocessAudio()
    {
        if (!assetPath.StartsWith("Assets/Resources/Audio/Voice/"))
            return;

        AudioImporter importer = (AudioImporter)assetImporter;
        importer.forceToMono = false;
        importer.loadInBackground = false;
        importer.defaultSampleSettings = new AudioImporterSampleSettings
        {
            loadType = AudioClipLoadType.DecompressOnLoad,
            compressionFormat = AudioCompressionFormat.PCM,
            quality = 1f,
            sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate,
            preloadAudioData = true
        };
    }
}
