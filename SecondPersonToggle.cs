using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using Zorro.Core;
using Zorro.Settings;
using Zorro.ControllerSupport;
using Photon.Pun;
using System.Linq;

namespace Nazo.SecondPersonToggle
{
    [BepInPlugin(GUID, ModName, Version)]
    public class SecondPersonToggle : BaseUnityPlugin  // 类名改为SecondPersonToggle以更准确
    {
        public const string GUID = "nazo.SecondPersonToggle";  // 更新GUID
        public const string ModName = "SecondPersonToggle";     // 更新Mod名称
        public const string Version = "1.0.11";                 // 更新版本号

        private ConfigEntry<KeyCode> toggleKey;
        private bool secondPersonEnabled;                      // 改为第二人称
        private MouseSensitivitySetting mouseSensSetting;
        private ControllerSensitivitySetting controllerSensSetting;
        private bool settingsInitialized;

        private readonly float height = 1.5f;
        private readonly float defaultDistance = 3f;
        private float currentDistance;
        private readonly float minDistance = 2f;
        private readonly float maxDistance = 4f;
        private readonly float zoomSpeed = 1f;
        private readonly float lerpRate = 5f;
        private readonly float turnSpeed = 720f;
        private readonly float clipRadius = 0.06f;
        private readonly float clipBuffer = 0.03f;
        private readonly LayerMask clipMask = LayerMask.GetMask("Terrain", "Map");

        void Awake()
        {
            toggleKey = Config.Bind("Camera", "ToggleKey", KeyCode.V, "Key to toggle camera view");

            currentDistance = defaultDistance;

            On.MainCameraMovement.LateUpdate += MainCameraMovement_LateUpdate;
            On.MainCameraMovement.CharacterCam += MainCameraMovement_CharacterCam;
            On.CharacterClimbing.TryToStartWallClimb += CharacterClimbing_TryToStartWallClimb;
        }

        private void EnsureSettings()
        {
            if (!settingsInitialized && GameHandler.Instance != null && GameHandler.Instance.SettingsHandler != null)
            {
                mouseSensSetting = GameHandler.Instance.SettingsHandler.GetSetting<MouseSensitivitySetting>();
                controllerSensSetting = GameHandler.Instance.SettingsHandler.GetSetting<ControllerSensitivitySetting>();
                settingsInitialized = true;
            }
        }

        private void MainCameraMovement_LateUpdate(On.MainCameraMovement.orig_LateUpdate orig, MainCameraMovement self)
        {
            if (Input.GetKeyDown(toggleKey.Value))
            {
                secondPersonEnabled = !secondPersonEnabled;
            }

            if (secondPersonEnabled)
            {
                float scroll = Input.mouseScrollDelta.y;
                if (Mathf.Abs(scroll) > 0.01f)
                    currentDistance = Mathf.Clamp(currentDistance - scroll * zoomSpeed, minDistance, maxDistance);
            }

            orig(self);
        }

        private void MainCameraMovement_CharacterCam(On.MainCameraMovement.orig_CharacterCam orig, MainCameraMovement self)
        {
            if (secondPersonEnabled && Character.localCharacter != null)
            {
                EnsureSettings();
                var camComp = self.GetComponent<MainCamera>();
                camComp.cam.fieldOfView = self.GetFov();

                Transform torso = Character.localCharacter.GetBodypart(BodypartType.Torso).transform;
                Vector3 lookDir = Character.localCharacter.data.lookDirection;
                if (lookDir == Vector3.zero)
                    lookDir = torso.forward;

                Vector3 desiredPosition;

                // 第二人称：相机在角色前方，看向角色
                // 将相机放在角色面前
                desiredPosition = torso.position + Vector3.up * height + lookDir.normalized * currentDistance;

                // 碰撞检测 - 防止相机穿墙
                Vector3 dirToCamera = (desiredPosition - torso.position).normalized;
                float maxDist = Vector3.Distance(desiredPosition, torso.position);

                if (Physics.SphereCast(torso.position, clipRadius, dirToCamera, out RaycastHit hit, maxDist, clipMask))
                {
                    // 如果检测到碰撞，将相机放在碰撞点前方
                    desiredPosition = hit.point - dirToCamera * clipBuffer;
                }

                // 平滑移动相机
                self.transform.position = Vector3.Lerp(self.transform.position, desiredPosition, Time.deltaTime * lerpRate);

                // 相机看向角色
                Vector3 lookAtPoint = torso.position + Vector3.up * 0.5f; // 稍微向上看角色的中心
                Quaternion desiredRotation = Quaternion.LookRotation(lookAtPoint - self.transform.position, Vector3.up);

                // 应用灵敏度设置
                float sens = settingsInitialized
                    ? (InputHandler.GetCurrentUsedInputScheme() == InputScheme.Gamepad
                        ? controllerSensSetting.Value
                        : mouseSensSetting.Value)
                    : 1f;

                self.transform.rotation = Quaternion.RotateTowards(self.transform.rotation, desiredRotation, turnSpeed * sens * Time.deltaTime);

                return;
            }

            orig(self);
        }

        private void CharacterClimbing_TryToStartWallClimb(On.CharacterClimbing.orig_TryToStartWallClimb orig, CharacterClimbing self, bool forceAttempt, Vector3 overide, bool botGrab, float raycastDistance)
        {
            Transform torso = self.character.GetBodypart(BodypartType.Torso).transform;
            var cam = MainCamera.instance.transform;
            Vector3 oldPos = cam.position;

            // 临时将相机放在角色背后，以便攀爬检测能正常工作
            Vector3 lookDir = self.character.data.lookDirection;
            if (lookDir == Vector3.zero)
                lookDir = torso.forward;

            cam.position = torso.position - lookDir.normalized * 1f; // 临时放在背后

            orig(self, forceAttempt, overide, botGrab, raycastDistance);
            cam.position = oldPos;
        }

    }

    public static class EnumerableExtensions
    {
        public static void ForEachTry<T>(this IEnumerable<T> list, Action<T> action, IDictionary<T, Exception> exceptions = null)
        {
            list.ToList().ForEach(element =>
            {
                try { action(element); }
                catch (Exception e) { exceptions?.Add(element, e); }
            });
        }
    }
}
