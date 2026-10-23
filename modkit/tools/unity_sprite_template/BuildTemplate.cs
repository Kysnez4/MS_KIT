// Шаблон бандла спрайтов ModKit: сцена с выключенными объектами mk_000..mk_255, у каждого SpriteRenderer со спрайтом FullRect
// на своей текстуре 4x4 RGBA32. pack_mod.py (UnityPy) подменяет текстуры, размеры и имена. Запуск:
//   Unity.exe -batchmode -quit -projectPath <проект> -buildTarget Android -executeMethod BuildTemplate.Build -logFile -
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BuildTemplate
{
    const int Slots = 256;

    public static void Build()
    {
        Directory.CreateDirectory("Assets/Slots");
        var px = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var cols = new Color32[16];
        for (int i = 0; i < 16; i++) cols[i] = new Color32(255, 0, 255, 255);
        px.SetPixels32(cols);
        var png = px.EncodeToPNG();
        for (int i = 0; i < Slots; i++) File.WriteAllBytes($"Assets/Slots/s{i:000}.png", png);
        AssetDatabase.Refresh();
        for (int i = 0; i < Slots; i++)
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath($"Assets/Slots/s{i:000}.png");
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.mipmapEnabled = false;
            imp.isReadable = false;
            imp.alphaIsTransparency = true;
            imp.filterMode = FilterMode.Bilinear;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.spritePixelsPerUnit = 100;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            var s = new TextureImporterSettings();
            imp.ReadTextureSettings(s);
            s.spriteMeshType = SpriteMeshType.FullRect;
            s.spriteGenerateFallbackPhysicsShape = false;
            s.spriteAlignment = (int)SpriteAlignment.Center;
            imp.SetTextureSettings(s);
            var android = imp.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.format = TextureImporterFormat.RGBA32;
            imp.SetPlatformTextureSettings(android);
            imp.SaveAndReimport();
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        for (int i = 0; i < Slots; i++)
        {
            var go = new GameObject($"mk_{i:000}");
            go.AddComponent<SpriteRenderer>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Slots/s{i:000}.png");
            go.SetActive(false);
        }
        EditorSceneManager.SaveScene(scene, "Assets/ModKitSprites.unity");
        AssetImporter.GetAtPath("Assets/ModKitSprites.unity").assetBundleName = "modkit_sprites";

        Directory.CreateDirectory("Build");
        BuildPipeline.BuildAssetBundles("Build", BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.Android);
        Debug.Log("BuildTemplate: OK " + new FileInfo("Build/modkit_sprites").Length);
    }
}
