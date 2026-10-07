using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BacardiBeatMakerSetup
{
    [MenuItem("Bacardi/Create Beat Maker Scene")]
    public static void CreateScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Bacardi Beat Maker").AddComponent<BacardiBeatMaker>();
        var camera = new GameObject("Main Camera").AddComponent<Camera>();
        camera.clearFlags=CameraClearFlags.SolidColor;
        camera.backgroundColor=new Color(.045f,.055f,.08f);
        camera.gameObject.AddComponent<AudioListener>();
        EditorSceneManager.SaveScene(scene,"Assets/Scenes/BacardiBeatMaker.unity");
        Selection.activeObject=GameObject.Find("Bacardi Beat Maker");
    }
}
