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

    private const float GestureHoldDuration = 1f;
    private const float GestureCooldownDuration = 1f;
    private const float ExtendedFingerThreshold = 0.1f;
    private const float CurledFingerThreshold = 0.09f;
    private const float FingerUpAlignmentThreshold = 0.6f;
    private static readonly Vector3 ChaseOffset = new Vector3(0f, 2.2f, -5.5f);
    private static readonly Vector3 CockpitLocalPosition = new Vector3(0f, 0f, 0f);

    private readonly XRHandJointID[] extendedGestureJoints =
    {
        XRHandJointID.IndexTip,
        XRHandJointID.MiddleTip
    };

    private readonly XRHandJointID[] curledGestureJoints =
    {
        XRHandJointID.RingTip,
        XRHandJointID.LittleTip
    };

    private Transform droneRoot;
    private Camera viewCamera;
    private Transform cameraOffset;
    private XRHandSubsystem handSubsystem;
    private GameObject cockpitVisual;
    private GameObject droneVisual;
    private Vector3 defaultCameraOffsetLocalPosition;
    private Quaternion defaultCameraOffsetLocalRotation;
    private ViewMode currentMode;
    private float gestureHoldTime;
    private float gestureCooldownUntil;
    private bool initialized;

    public string CurrentModeLabel => currentMode switch
    {
        ViewMode.Pilot => "Pilot",
        ViewMode.Cockpit => "Cockpit",
        ViewMode.Chase => "Chase",
        _ => "Unknown"
    };

    public string GestureHint => "Hold one hand with index + middle fingers pointing up for 1s to switch views.";

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

        if (handSubsystem == null || Time.time < gestureCooldownUntil)
        {
            return;
        }

        if (!IsCycleGestureActive())
        {
            gestureHoldTime = 0f;
            return;
        }

        gestureHoldTime += Time.deltaTime;
        if (gestureHoldTime < GestureHoldDuration)
        {
            return;
        }

        gestureHoldTime = 0f;
        gestureCooldownUntil = Time.time + GestureCooldownDuration;
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

        return IsTwoFingersUp(leftHand) || IsTwoFingersUp(rightHand);
    }

    private static bool TryGetPalmPose(XRHand hand, out Pose pose)
    {
        return hand.GetJoint(XRHandJointID.Palm).TryGetPose(out pose);
    }

    private bool IsTwoFingersUp(XRHand hand)
    {
        if (!hand.isTracked || !TryGetPalmPose(hand, out var palmPose))
        {
            return false;
        }

        var palmUp = Vector3.Dot(palmPose.rotation * Vector3.up, Vector3.up);
        if (palmUp < 0.25f)
        {
            return false;
        }

        foreach (var jointId in extendedGestureJoints)
        {
            if (!IsFingerExtendedUp(hand, jointId, palmPose.position))
            {
                return false;
            }
        }

        foreach (var jointId in curledGestureJoints)
        {
            if (!hand.GetJoint(jointId).TryGetPose(out var jointPose))
            {
                return false;
            }

            if (Vector3.Distance(jointPose.position, palmPose.position) > CurledFingerThreshold)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsFingerExtendedUp(XRHand hand, XRHandJointID jointId, Vector3 palmPosition)
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

        var fingerDirection = fingerVector.normalized;
        return Vector3.Distance(jointPose.position, palmPosition) > ExtendedFingerThreshold &&
               Vector3.Dot(fingerDirection, Vector3.up) > FingerUpAlignmentThreshold;
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
}
