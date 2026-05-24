using CPS.Lab10.UR5e;
using CPS.Lab11.MobileManipulator;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CPS.Lab11.MobileManipulator.EditorTools
{
    [InitializeOnLoad]
    internal static class UR5ePosePresetPlayModeGuard
    {
        static UR5ePosePresetPlayModeGuard()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode)
            {
                return;
            }

            foreach (UR5ePosePresetPlayer player in Resources.FindObjectsOfTypeAll<UR5ePosePresetPlayer>())
            {
                if (!UR5ePosePresetPlayerEditor.IsSceneObject(player))
                {
                    continue;
                }

                UR5ePosePresetPlayerEditor.ApplyPoseInEditMode(player, new UR5eJointPose());
            }
        }
    }

    [CustomEditor(typeof(UR5ePosePresetPlayer))]
    public class UR5ePosePresetPlayerEditor : Editor
    {
        private static readonly string[] JointLabels =
        {
            "Base",
            "Shoulder",
            "Elbow",
            "Wrist 1",
            "Wrist 2",
            "Wrist 3"
        };

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            UR5ePosePresetPlayer player = (UR5ePosePresetPlayer)target;

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Joint Jog", EditorStyles.boldLabel);
            DrawJointJog(player);

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Pose Tuning", EditorStyles.boldLabel);

            DrawPoseRow(player, MobileManipulatorPose.Home);
            DrawPoseRow(player, MobileManipulatorPose.Approach);
            DrawPoseRow(player, MobileManipulatorPose.Pick);
            DrawPoseRow(player, MobileManipulatorPose.Carry);
            DrawPoseRow(player, MobileManipulatorPose.Place);

            EditorGUILayout.Space(6f);
            using (new EditorGUI.DisabledScope(player.JointController == null))
            {
                if (GUILayout.Button("Restore Reference Zero Pose"))
                {
                    ApplyPoseInEditMode(player, new UR5eJointPose());
                }

                if (GUILayout.Button("Log Current Joint Angles"))
                {
                    LogCurrentJointAngles(player);
                }
            }
        }

        private static void DrawPoseRow(UR5ePosePresetPlayer player, MobileManipulatorPose poseName)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(poseName.ToString(), GUILayout.Width(72f));

                using (new EditorGUI.DisabledScope(player.JointController == null))
                {
                    if (GUILayout.Button("Preview", GUILayout.Width(80f)))
                    {
                        PreviewPose(player, poseName);
                    }

                    if (GUILayout.Button("Save Current", GUILayout.Width(110f)))
                    {
                        SaveCurrentPose(player, poseName);
                    }
                }

                if (Application.isPlaying)
                {
                    if (GUILayout.Button("Play", GUILayout.Width(60f)))
                    {
                        player.PlayPose(player.GetPose(poseName));
                    }
                }
            }
        }

        private static void DrawJointJog(UR5ePosePresetPlayer player)
        {
            UR5eJointController controller = player.JointController;
            using (new EditorGUI.DisabledScope(controller == null))
            {
                for (int i = 0; i < UR5eJointPose.JointCount; i++)
                {
                    float currentAngle = controller != null ? controller.GetJointAngle(i) : 0f;
                    EditorGUI.BeginChangeCheck();
                    float nextAngle = EditorGUILayout.Slider(JointLabels[i], currentAngle, -180f, 180f);
                    if (EditorGUI.EndChangeCheck())
                    {
                        SetJointAngle(player, i, nextAngle);
                    }
                }
            }
        }

        private static void PreviewPose(UR5ePosePresetPlayer player, MobileManipulatorPose poseName)
        {
            Undo.RecordObject(player, "Preview " + poseName + " Pose");
            RecordArmObjects(player, "Preview " + poseName + " Pose");

            if (Application.isPlaying)
            {
                player.ApplyPoseInstant(poseName);
            }
            else
            {
                ApplyPoseInEditMode(player, player.GetPose(poseName));
            }

            MarkArmObjectsDirty(player);
            SceneView.RepaintAll();
        }

        private static void SaveCurrentPose(UR5ePosePresetPlayer player, MobileManipulatorPose poseName)
        {
            Undo.RecordObject(player, "Save " + poseName + " Pose");

            if (player.SaveCurrentAsPose(poseName))
            {
                EditorUtility.SetDirty(player);
            }
        }

        private static void SetJointAngle(UR5ePosePresetPlayer player, int jointIndex, float angle)
        {
            UR5eJointController controller = player.JointController;
            if (controller == null)
            {
                return;
            }

            UR5eJointPose pose = controller.GetCurrentPose();
            pose.SetAngle(jointIndex, angle);

            RecordArmObjects(player, "Jog UR5e Joint");
            if (Application.isPlaying)
            {
                controller.SetPose(pose);
            }
            else
            {
                ApplyPoseInEditMode(player, pose);
            }

            MarkArmObjectsDirty(player);
            SceneView.RepaintAll();
        }

        internal static bool IsSceneObject(UR5ePosePresetPlayer player)
        {
            return player != null
                && player.gameObject.scene.IsValid()
                && !EditorUtility.IsPersistent(player);
        }

        internal static void ApplyPoseInEditMode(UR5ePosePresetPlayer player, UR5eJointPose pose)
        {
            UR5eJointController controller = player.JointController;
            if (controller == null || pose == null)
            {
                return;
            }

            UR5eJoint[] joints = controller.GetComponentsInChildren<UR5eJoint>(true);
            player.SetReferencePose(joints, GetReferenceLocalRotations(joints));
            player.ApplyPoseInstant(pose);
            EditorUtility.SetDirty(player);
            if (player.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
            }
        }

        private static Quaternion[] GetReferenceLocalRotations(UR5eJoint[] joints)
        {
            Quaternion[] rotations = new Quaternion[joints.Length];
            for (int i = 0; i < joints.Length; i++)
            {
                UR5eJoint joint = joints[i];
                if (joint == null)
                {
                    continue;
                }

                Transform jointTransform = joint.JointTransform != null ? joint.JointTransform : joint.transform;
                Transform sourceTransform = PrefabUtility.GetCorrespondingObjectFromSource(jointTransform);
                rotations[i] = sourceTransform != null ? sourceTransform.localRotation : jointTransform.localRotation;
            }

            return rotations;
        }

        private static void LogCurrentJointAngles(UR5ePosePresetPlayer player)
        {
            UR5eJointPose pose = player.JointController.GetCurrentPose();
            Debug.Log(
                "Current UR5e pose: "
                + $"Base={pose.BaseJoint:F2}, "
                + $"Shoulder={pose.Shoulder:F2}, "
                + $"Elbow={pose.Elbow:F2}, "
                + $"Wrist1={pose.Wrist1:F2}, "
                + $"Wrist2={pose.Wrist2:F2}, "
                + $"Wrist3={pose.Wrist3:F2}",
                player);
        }

        private static void RecordArmObjects(UR5ePosePresetPlayer player, string undoName)
        {
            UR5eJointController controller = player.JointController;
            if (controller == null)
            {
                return;
            }

            Undo.RecordObjects(controller.GetComponentsInChildren<Transform>(true), undoName);
            Undo.RecordObjects(controller.GetComponentsInChildren<UR5eJoint>(true), undoName);
        }

        private static void MarkArmObjectsDirty(UR5ePosePresetPlayer player)
        {
            UR5eJointController controller = player.JointController;
            if (controller == null)
            {
                return;
            }

            foreach (Transform child in controller.GetComponentsInChildren<Transform>(true))
            {
                EditorUtility.SetDirty(child);
            }

            foreach (UR5eJoint joint in controller.GetComponentsInChildren<UR5eJoint>(true))
            {
                EditorUtility.SetDirty(joint);
            }
        }
    }
}
