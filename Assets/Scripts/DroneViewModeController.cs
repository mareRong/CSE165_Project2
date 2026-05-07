using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

public class DroneViewModeController : MonoBehaviour
{
    private enum ViewMode
    {
        Pilot,
        Cockpit,
        Chase
    }

    private const float InitialGestureHoldDuration = 0.8f;
    private const float HeldGestureCycleInterval = 0.9f;
    private const float ExtendedFingerThreshold = 0.075f;
    private const float CurledFingerThreshold = 0.11f;
    private const float FingerRaisedHeightThreshold = 0.035f;
    private const float LowerFingerHeightMargin = 0.015f;
    private const float FingerSeparationThreshold = 0.02f;
    private const string CockpitResourcePath = "drone_design";
    private static readonly Vector3 ChaseOffset = new Vector3(0f, 2.2f, -5.5f);
    private static readonly Vector3 CockpitLocalPosition = new Vector3(0f, 0f, 0f);
    private static readonly Vector3 ImportedCockpitLocalPosition = new Vector3(0f, -0.55f, 0.8f);
    private static readonly Vector3 ImportedCockpitLocalRotation = new Vector3(0f, 180f, 0f);
    private static readonly Vector3 ImportedCockpitLocalScale = new Vector3(0.25f, 0.25f, 0.25f);

    private Transform droneRoot;
    private Camera viewCamera;
    private Transform cameraOffset;
    private XRHandSubsystem handSubsystem;
    private GameObject cockpitVisual;
    private GameObject droneVisual;
    private GameObject importedCockpitPrefab;
    private Vector3 defaultCameraOffsetLocalPosition;
    private Quaternion defaultCameraOffsetLocalRotation;
    private ViewMode currentMode;
    private float gestureHoldTime;
    private bool hasTriggeredWhileHeld;
    private bool initialized;

    public string CurrentModeLabel => currentMode switch
    {
        ViewMode.Pilot => "Pilot",
        ViewMode.Cockpit => "Cockpit",
        ViewMode.Chase => "Chase",
        _ => "Unknown"
    };

    public string GestureHint => "Hold one hand with index + middle fingers up to keep cycling views.";

    public void Initialize(Transform root, Camera cameraToUse)
    {
        if (root == null || cameraToUse == null)
        {
            return;
        }

        droneRoot = root;
        viewCamera = cameraToUse;
        cameraOffset = viewCamera.transform.parent;
        defaultCameraOffsetLocalPosition = cameraOffset != null ? cameraOffset.localPosition : Vector3.zero;
        defaultCameraOffsetLocalRotation = cameraOffset != null ? cameraOffset.localRotation : Quaternion.identity;

        EnsureVisuals();

        if (!initialized)
        {
            currentMode = ViewMode.Pilot;
            initialized = true;
        }

        ApplyViewMode(currentMode);
    }

    private void Update()
    {
        if (!initialized)
        {
            return;
        }

        if (handSubsystem == null)
        {
            var manager = XRGeneralSettings.Instance?.Manager;
            if (manager?.activeLoader != null)
            {
                handSubsystem = manager.activeLoader.GetLoadedSubsystem<XRHandSubsystem>();
            }
        }

        if (handSubsystem == null)
        {
            return;
        }

        if (!IsCycleGestureActive())
        {
            gestureHoldTime = 0f;
            hasTriggeredWhileHeld = false;
            return;
        }

        gestureHoldTime += Time.deltaTime;
        var requiredHoldTime = hasTriggeredWhileHeld
            ? HeldGestureCycleInterval
            : InitialGestureHoldDuration;

        if (gestureHoldTime < requiredHoldTime)
        {
            return;
        }

        gestureHoldTime = 0f;
        hasTriggeredWhileHeld = true;
        ApplyViewMode((ViewMode)(((int)currentMode + 1) % 3));
    }

    private void LateUpdate()
    {
        if (!initialized || cameraOffset == null)
        {
            return;
        }

        if (currentMode == ViewMode.Chase)
        {
            UpdateChaseCameraPose();
        }
    }

    private bool IsCycleGestureActive()
    {
        var leftHand = handSubsystem.leftHand;
        var rightHand = handSubsystem.rightHand;

        return IsTwoFingersUpGesture(leftHand) || IsTwoFingersUpGesture(rightHand);
    }

    private static bool TryGetPalmPose(XRHand hand, out Pose pose)
    {
        return hand.GetJoint(XRHandJointID.Palm).TryGetPose(out pose);
    }

    public static bool IsTwoFingersUpGesture(XRHand hand)
    {
        if (!hand.isTracked || !TryGetPalmPose(hand, out var palmPose))
        {
            return false;
        }

        if (!TryGetJointPose(hand, XRHandJointID.IndexTip, out var indexTipPose) ||
            !TryGetJointPose(hand, XRHandJointID.MiddleTip, out var middleTipPose) ||
            !TryGetJointPose(hand, XRHandJointID.RingTip, out var ringTipPose) ||
            !TryGetJointPose(hand, XRHandJointID.LittleTip, out var littleTipPose))
        {
            return false;
        }

        var indexExtended = IsFingerExtendedUp(hand, XRHandJointID.IndexTip, palmPose.position);
        var middleExtended = IsFingerExtendedUp(hand, XRHandJointID.MiddleTip, palmPose.position);
        if (!indexExtended || !middleExtended)
        {
            return false;
        }

        var indexHeight = indexTipPose.position.y - palmPose.position.y;
        var middleHeight = middleTipPose.position.y - palmPose.position.y;
        if (indexHeight < FingerRaisedHeightThreshold || middleHeight < FingerRaisedHeightThreshold)
        {
            return false;
        }

        var indexMiddleSeparation = Vector3.Distance(indexTipPose.position, middleTipPose.position);
        if (indexMiddleSeparation < FingerSeparationThreshold)
        {
            return false;
        }

        var ringDistance = Vector3.Distance(ringTipPose.position, palmPose.position);
        var littleDistance = Vector3.Distance(littleTipPose.position, palmPose.position);
        var ringLowerThanRaisedFingers =
            ringTipPose.position.y < indexTipPose.position.y - LowerFingerHeightMargin &&
            ringTipPose.position.y < middleTipPose.position.y - LowerFingerHeightMargin;
        var littleLowerThanRaisedFingers =
            littleTipPose.position.y < indexTipPose.position.y - LowerFingerHeightMargin &&
            littleTipPose.position.y < middleTipPose.position.y - LowerFingerHeightMargin;

        return ringDistance < CurledFingerThreshold &&
               littleDistance < CurledFingerThreshold &&
               ringLowerThanRaisedFingers &&
               littleLowerThanRaisedFingers;
    }

    private static bool IsFingerExtendedUp(XRHand hand, XRHandJointID jointId, Vector3 palmPosition)
    {
        if (!hand.GetJoint(jointId).TryGetPose(out var jointPose))
        {
            return false;
        }

        var fingerVector = jointPose.position - palmPosition;
        if (fingerVector.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        return Vector3.Distance(jointPose.position, palmPosition) > ExtendedFingerThreshold;
    }

    private static bool TryGetJointPose(XRHand hand, XRHandJointID jointId, out Pose pose)
    {
        return hand.GetJoint(jointId).TryGetPose(out pose);
    }

    private void EnsureVisuals()
    {
        if (cockpitVisual == null)
        {
            cockpitVisual = CreateCockpitVisual();
        }

        if (droneVisual == null)
        {
            droneVisual = CreateDroneVisual();
        }

        ReparentVisualsIfNeeded();
    }

    private void ReparentVisualsIfNeeded()
    {
        if (cockpitVisual != null && cameraOffset != null && cockpitVisual.transform.parent != cameraOffset)
        {
            cockpitVisual.transform.SetParent(cameraOffset, false);
            cockpitVisual.transform.localPosition = CockpitLocalPosition;
            cockpitVisual.transform.localRotation = Quaternion.identity;
        }

        if (droneVisual != null && droneRoot != null && droneVisual.transform.parent != droneRoot)
        {
            droneVisual.transform.SetParent(droneRoot, false);
            droneVisual.transform.localPosition = Vector3.zero;
            droneVisual.transform.localRotation = Quaternion.identity;
        }
    }

    private GameObject CreateCockpitVisual()
    {
        var importedCockpit = TryCreateImportedCockpitVisual();
        if (importedCockpit != null)
        {
            importedCockpit.SetActive(false);
            return importedCockpit;
        }

        var cockpitRoot = new GameObject("Virtual Cockpit");
        cockpitRoot.transform.localPosition = CockpitLocalPosition;
        cockpitRoot.transform.localRotation = Quaternion.identity;

        CreatePrimitive(cockpitRoot.transform, PrimitiveType.Cube, "Dash", new Vector3(0f, -0.32f, 0.45f), new Vector3(0.95f, 0.08f, 0.26f), new Color(0.14f, 0.14f, 0.16f));
        CreatePrimitive(cockpitRoot.transform, PrimitiveType.Cylinder, "Left Rail", new Vector3(-0.38f, -0.05f, 0.32f), new Vector3(0.025f, 0.3f, 0.025f), new Vector3(0f, 0f, 0f), Color.black);
        CreatePrimitive(cockpitRoot.transform, PrimitiveType.Cylinder, "Right Rail", new Vector3(0.38f, -0.05f, 0.32f), new Vector3(0.025f, 0.3f, 0.025f), new Vector3(0f, 0f, 0f), Color.black);
        CreatePrimitive(cockpitRoot.transform, PrimitiveType.Cube, "Canopy Front", new Vector3(0f, 0.12f, 0.74f), new Vector3(0.82f, 0.32f, 0.02f), new Color(0.52f, 0.82f, 0.95f, 0.45f));
        CreatePrimitive(cockpitRoot.transform, PrimitiveType.Cube, "Canopy Top", new Vector3(0f, 0.43f, 0.32f), new Vector3(0.84f, 0.02f, 0.92f), new Color(0.52f, 0.82f, 0.95f, 0.32f));
        CreatePrimitive(cockpitRoot.transform, PrimitiveType.Cube, "Seat Back", new Vector3(0f, 0.02f, -0.12f), new Vector3(0.38f, 0.42f, 0.04f), new Color(0.1f, 0.1f, 0.1f));
        CreatePrimitive(cockpitRoot.transform, PrimitiveType.Cube, "Seat Base", new Vector3(0f, -0.24f, 0.02f), new Vector3(0.34f, 0.04f, 0.3f), new Color(0.1f, 0.1f, 0.1f));

        cockpitRoot.SetActive(false);
        return cockpitRoot;
    }

    private GameObject TryCreateImportedCockpitVisual()
    {
        if (importedCockpitPrefab == null)
        {
            importedCockpitPrefab = Resources.Load<GameObject>(CockpitResourcePath);
        }

        if (importedCockpitPrefab == null)
        {
            return null;
        }

        var cockpitRoot = new GameObject("Virtual Cockpit");
        cockpitRoot.transform.localPosition = CockpitLocalPosition;
        cockpitRoot.transform.localRotation = Quaternion.identity;

        var cockpitInstance = Object.Instantiate(importedCockpitPrefab, cockpitRoot.transform);
        cockpitInstance.name = "Imported Drone Cockpit";
        cockpitInstance.transform.localPosition = ImportedCockpitLocalPosition;
        cockpitInstance.transform.localEulerAngles = ImportedCockpitLocalRotation;
        cockpitInstance.transform.localScale = ImportedCockpitLocalScale;
        StripColliders(cockpitRoot.transform);
        return cockpitRoot;
    }

    private GameObject CreateDroneVisual()
    {
        var droneRootVisual = new GameObject("Drone Body Visual");
        droneRootVisual.transform.localPosition = Vector3.zero;
        droneRootVisual.transform.localRotation = Quaternion.identity;

        CreatePrimitive(droneRootVisual.transform, PrimitiveType.Cube, "Core", Vector3.zero, new Vector3(0.3f, 0.08f, 0.3f), new Color(0.18f, 0.18f, 0.18f));
        CreatePrimitive(droneRootVisual.transform, PrimitiveType.Cylinder, "Arm A", Vector3.zero, new Vector3(0.03f, 0.35f, 0.03f), new Vector3(0f, 0f, 45f), new Color(0.1f, 0.1f, 0.1f));
        CreatePrimitive(droneRootVisual.transform, PrimitiveType.Cylinder, "Arm B", Vector3.zero, new Vector3(0.03f, 0.35f, 0.03f), new Vector3(0f, 0f, -45f), new Color(0.1f, 0.1f, 0.1f));
        CreatePrimitive(droneRootVisual.transform, PrimitiveType.Cube, "Nose", new Vector3(0f, 0.02f, 0.24f), new Vector3(0.08f, 0.05f, 0.12f), new Color(1f, 0.45f, 0.18f));

        var propOffsets = new[]
        {
            new Vector3(0.5f, 0f, 0.5f),
            new Vector3(-0.5f, 0f, 0.5f),
            new Vector3(0.5f, 0f, -0.5f),
            new Vector3(-0.5f, 0f, -0.5f)
        };

        for (var index = 0; index < propOffsets.Length; index++)
        {
            CreatePrimitive(
                droneRootVisual.transform,
                PrimitiveType.Sphere,
                $"Prop {index + 1}",
                propOffsets[index],
                Vector3.one * 0.12f,
                index < 2 ? new Color(0.16f, 0.16f, 0.16f) : new Color(0.22f, 0.22f, 0.22f));
        }

        droneRootVisual.SetActive(false);
        return droneRootVisual;
    }

    private void ApplyViewMode(ViewMode nextMode)
    {
        currentMode = nextMode;

        if (cameraOffset != null)
        {
            cameraOffset.localPosition = defaultCameraOffsetLocalPosition;
            cameraOffset.localRotation = defaultCameraOffsetLocalRotation;
        }

        if (cockpitVisual != null)
        {
            cockpitVisual.SetActive(nextMode == ViewMode.Cockpit);
        }

        if (droneVisual != null)
        {
            droneVisual.SetActive(nextMode == ViewMode.Chase);
        }

        if (nextMode == ViewMode.Chase && cameraOffset != null)
        {
            UpdateChaseCameraPose();
        }
    }

    private void UpdateChaseCameraPose()
    {
        cameraOffset.localPosition = defaultCameraOffsetLocalPosition + ChaseOffset;

        var lookDirection = droneRoot.position - cameraOffset.position;
        if (lookDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        cameraOffset.rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
    }

    private static void CreatePrimitive(
        Transform parent,
        PrimitiveType primitiveType,
        string objectName,
        Vector3 localPosition,
        Vector3 localScale)
    {
        CreatePrimitive(parent, primitiveType, objectName, localPosition, localScale, Vector3.zero, null);
    }

    private static void CreatePrimitive(
        Transform parent,
        PrimitiveType primitiveType,
        string objectName,
        Vector3 localPosition,
        Vector3 localScale,
        Color color)
    {
        CreatePrimitive(parent, primitiveType, objectName, localPosition, localScale, Vector3.zero, color);
    }

    private static void CreatePrimitive(
        Transform parent,
        PrimitiveType primitiveType,
        string objectName,
        Vector3 localPosition,
        Vector3 localScale,
        Vector3 localEulerAngles)
    {
        CreatePrimitive(parent, primitiveType, objectName, localPosition, localScale, localEulerAngles, null);
    }

    private static void CreatePrimitive(
        Transform parent,
        PrimitiveType primitiveType,
        string objectName,
        Vector3 localPosition,
        Vector3 localScale,
        Vector3 localEulerAngles,
        Color? color)
    {
        var child = GameObject.CreatePrimitive(primitiveType);
        child.name = objectName;
        child.transform.SetParent(parent, false);
        child.transform.localPosition = localPosition;
        child.transform.localScale = localScale;
        child.transform.localEulerAngles = localEulerAngles;

        if (color.HasValue)
        {
            var renderer = child.GetComponent<Renderer>();
            if (renderer != null)
            {
                var shader = Shader.Find("Standard");
                if (shader == null)
                {
                    shader = Shader.Find("Universal Render Pipeline/Lit");
                }

                if (shader == null)
                {
                    shader = Shader.Find("Unlit/Color");
                }

                if (shader != null)
                {
                    var material = new Material(shader);
                    material.color = color.Value;
                    renderer.material = material;
                }
            }
        }

        var collider = child.GetComponent<Collider>();
        if (collider != null)
        {
            Object.Destroy(collider);
        }
    }

    private static void StripColliders(Transform root)
    {
        foreach (var collider in root.GetComponentsInChildren<Collider>(true))
        {
            Object.Destroy(collider);
        }
    }
}
