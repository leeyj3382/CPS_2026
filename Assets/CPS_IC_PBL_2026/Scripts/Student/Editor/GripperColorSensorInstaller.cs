using CPS.ICPBL.Student;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CPS.ICPBL.Student.EditorTools
{
    public static class GripperColorSensorInstaller
    {
        private const string ScenePath = "Assets/CPS_IC_PBL_2026/Scene/ICPBL_2026.unity";
        private const string SensorPrefabPath =
            "Assets/CPS_IC_PBL_2026/Scripts/Student/Vision/Prefabs/ColorSensor.prefab";

        [MenuItem("CPS/Student/Install Gripper Color Sensors")]
        public static void Install()
        {
            EditorSceneManager.OpenScene(ScenePath);

            StudentSceneReferences sceneReferences =
                Object.FindObjectOfType<StudentSceneReferences>();
            if (sceneReferences == null)
            {
                throw new System.InvalidOperationException(
                    "StudentSceneReferences was not found in the scene.");
            }

            global::ColorSensor sensorPrefab =
                AssetDatabase.LoadAssetAtPath<global::ColorSensor>(SensorPrefabPath);
            if (sensorPrefab == null)
            {
                throw new System.InvalidOperationException(
                    "ColorSensor prefab was not found: " + SensorPrefabPath);
            }

            InstallForRobot(
                "RobotA",
                sceneReferences,
                sceneReferences.RobotAAgent,
                sceneReferences.RobotAColorSensor,
                sceneReferences.RobotAColorArea,
                "robotAColorSensor",
                "robotAColorArea",
                sensorPrefab);

            InstallForRobot(
                "RobotB",
                sceneReferences,
                sceneReferences.RobotBAgent,
                sceneReferences.RobotBColorSensor,
                sceneReferences.RobotBColorArea,
                "robotBColorSensor",
                "robotBColorArea",
                sensorPrefab);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("Installed gripper-mounted color sensors.");
        }

        private static void InstallForRobot(
            string label,
            StudentSceneReferences sceneReferences,
            RobotAgent agent,
            global::ColorSensor oldSensor,
            global::ColorArea oldArea,
            string sceneSensorField,
            string sceneAreaField,
            global::ColorSensor sensorPrefab)
        {
            if (oldSensor == null && oldArea == null)
            {
                throw new System.InvalidOperationException(
                    label + " has no ColorSensor or ColorArea reference.");
            }

            Transform parent = oldSensor != null
                ? oldSensor.transform.parent
                : oldArea.transform.parent;
            if (parent == null)
            {
                throw new System.InvalidOperationException(
                    label + " color sensor has no parent transform.");
            }

            int siblingIndex = oldSensor != null
                ? oldSensor.transform.GetSiblingIndex()
                : oldArea.transform.GetSiblingIndex();

            global::ColorSensor newSensor = CreateSensorInstance(
                sensorPrefab,
                parent,
                siblingIndex);
            global::ColorArea newArea =
                newSensor.GetComponentInChildren<global::ColorArea>(true);
            if (newArea == null)
            {
                throw new System.InvalidOperationException(
                    "ColorSensor prefab has no child ColorArea.");
            }

            CopySensorConfiguration(oldSensor, oldArea, newSensor, newArea);
            SetObjectReference(sceneReferences, sceneSensorField, newSensor);
            SetObjectReference(sceneReferences, sceneAreaField, newArea);

            if (agent != null)
            {
                SetObjectReference(agent, "colorSensor", newSensor);
                SetObjectReference(agent, "colorArea", newArea);
            }

            if (oldSensor != null)
            {
                Object.DestroyImmediate(oldSensor.gameObject);
            }
            else if (oldArea != null)
            {
                Object.DestroyImmediate(oldArea.gameObject);
            }
        }

        private static global::ColorSensor CreateSensorInstance(
            global::ColorSensor sensorPrefab,
            Transform parent,
            int siblingIndex)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(
                sensorPrefab.gameObject,
                parent);
            instance.name = "ColorSensor";
            instance.transform.SetSiblingIndex(siblingIndex);

            global::ColorSensor sensor = instance.GetComponent<global::ColorSensor>();
            if (sensor == null)
            {
                throw new System.InvalidOperationException(
                    "Instantiated ColorSensor prefab has no ColorSensor component.");
            }

            return sensor;
        }

        private static void CopySensorConfiguration(
            global::ColorSensor oldSensor,
            global::ColorArea oldArea,
            global::ColorSensor newSensor,
            global::ColorArea newArea)
        {
            if (oldSensor != null)
            {
                newSensor.transform.localPosition = oldSensor.transform.localPosition;
                newSensor.transform.localRotation = oldSensor.transform.localRotation;
                newSensor.transform.localScale = oldSensor.transform.localScale;

                newSensor.width = oldSensor.width;
                newSensor.height = oldSensor.height;
                newSensor.length = oldSensor.length;
                newSensor.lateralOffset = oldSensor.lateralOffset;
                newSensor.verticalOffset = oldSensor.verticalOffset;
                newSensor.distance = oldSensor.distance;
                newSensor.localEulerAngles = oldSensor.localEulerAngles;
                newSensor.applyOnStart = oldSensor.applyOnStart;
                newSensor.applyOnValidate = oldSensor.applyOnValidate;

                if (oldArea == null)
                {
                    oldArea = oldSensor.area;
                }
            }

            newSensor.area = newArea;

            if (oldArea == null)
            {
                return;
            }

            newArea.transform.localPosition = oldArea.transform.localPosition;
            newArea.transform.localRotation = oldArea.transform.localRotation;
            newArea.transform.localScale = oldArea.transform.localScale;

            newArea.color = oldArea.color;
            newArea.defaultColor = oldArea.defaultColor;
            newArea.ignoreSameRoot = oldArea.ignoreSameRoot;
            newArea.requiredTag = oldArea.requiredTag;
            newArea.detectableLayers = oldArea.detectableLayers;
            newArea.searchRendererInParents = oldArea.searchRendererInParents;

            BoxCollider oldCollider = oldArea.GetComponent<BoxCollider>();
            BoxCollider newCollider = newArea.GetComponent<BoxCollider>();
            if (oldCollider == null || newCollider == null)
            {
                return;
            }

            newCollider.enabled = oldCollider.enabled;
            newCollider.isTrigger = oldCollider.isTrigger;
            newCollider.center = oldCollider.center;
            newCollider.size = oldCollider.size;
            newCollider.sharedMaterial = oldCollider.sharedMaterial;
        }

        private static void SetObjectReference(
            Object target,
            string fieldName,
            Object value)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(fieldName);
            if (property == null)
            {
                throw new System.InvalidOperationException(
                    target.name + " has no serialized field: " + fieldName);
            }

            property.objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }
    }
}
