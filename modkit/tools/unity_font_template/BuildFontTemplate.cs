// Шаблон бандла шрифта ModKit: сцена с выключенным объектом mk_font, у него UI.Text с динамическим шрифтом (TTF внутри).
// pack_mod.py (font_bundle.py, UnityPy) подменяет байты TTF и имя шрифта. Нужен пакет com.unity.ugui. Запуск:
//   Unity.exe -batchmode -quit -projectPath <проект> -buildTarget Android -executeMethod BuildFontTemplate.Build -logFile -
// В Assets/Font/slot.ttf положить любой TTF (заглушка, байты потом вычищает font_bundle.py --strip).
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class BuildFontTemplate
{
    public static void Build()
    {
        var imp = (TrueTypeFontImporter)AssetImporter.GetAtPath("Assets/Font/slot.ttf");
        imp.fontTextureCase = FontTextureCase.Dynamic;
        imp.includeFontData = true;
        imp.fontNames = new[] { "ModKitFont" };
        imp.SaveAndReimport();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var go = new GameObject("mk_font", typeof(RectTransform));
        go.AddComponent<Text>().font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Font/slot.ttf");
        go.SetActive(false);
        EditorSceneManager.SaveScene(scene, "Assets/ModKitFont.unity");
        AssetImporter.GetAtPath("Assets/ModKitFont.unity").assetBundleName = "modkit_font";
        AssetImporter.GetAtPath("Assets/ModKitSprites.unity").assetBundleName = "";

        Directory.CreateDirectory("Build");
        BuildPipeline.BuildAssetBundles("Build", BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.Android);
        Debug.Log("BuildFontTemplate: OK " + new FileInfo("Build/modkit_font").Length);
    }
}
