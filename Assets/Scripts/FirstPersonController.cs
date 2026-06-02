using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float walkSpeed = 4.0f;
    public float gravity = 20.0f;
    public float pushForce = 3.0f;

    [Header("Look Settings")]
    public float mouseSensitivity = 2.0f;
    public float upLimit = -80.0f;
    public float downLimit = 80.0f;

    private CharacterController controller;
    private Camera playerCamera;
    private Light flashlight;

    private float rotationX = 0f;
    private float verticalVelocity = 0f;

    private void Start()
    {
        controller = GetComponent<CharacterController>();
        playerCamera = GetComponentInChildren<Camera>();

        // Lock cursor
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Initialize flashlight
        SetupFlashlight();
    }

    private void SetupFlashlight()
    {
        // Try to locate existing light child first
        flashlight = GetComponentInChildren<Light>();
        if (flashlight == null)
        {
            GameObject flGo = new GameObject("PlayerFlashlight");
            Transform parentTransform = playerCamera != null ? playerCamera.transform : transform;
            flGo.transform.parent = parentTransform;
            flGo.transform.localPosition = new Vector3(0, 0, 0.2f);
            flGo.transform.localRotation = Quaternion.identity;

            flashlight = flGo.AddComponent<Light>();
            flashlight.type = LightType.Spot;
            flashlight.range = 20f;
            flashlight.spotAngle = 60f;
            flashlight.intensity = 2.5f;
            flashlight.color = new Color(1.0f, 0.93f, 0.82f); // Retro halogen/tungsten flashlight color
            flashlight.enabled = true;
        }
    }

    private void Update()
    {
        HandleMouseLook();
        HandleMovement();
        HandleFlashlightToggle();
        HandleInteraction();
    }

    private void HandleInteraction()
    {
        if (Input.GetKeyDown(KeyCode.E))
        {
            if (playerCamera == null) return;

            Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, 2.5f))
            {
                Rigidbody rb = hit.collider.attachedRigidbody;
                if (rb != null && !rb.isKinematic)
                {
                    // Apply physical impulse force to swing the door open
                    Vector3 pushDirection = playerCamera.transform.forward;
                    rb.AddForceAtPosition(pushDirection * 12.0f, hit.point, ForceMode.Impulse);
                }
            }
        }
    }

    private void HandleMouseLook()
    {
        if (playerCamera == null) return;

        float rotateAboutY = Input.GetAxis("Mouse X") * mouseSensitivity;
        transform.Rotate(0, rotateAboutY, 0);

        rotationX -= Input.GetAxis("Mouse Y") * mouseSensitivity;
        rotationX = Mathf.Clamp(rotationX, upLimit, downLimit);
        playerCamera.transform.localRotation = Quaternion.Euler(rotationX, 0, 0);
    }

    private void HandleMovement()
    {
        // Check if player is grounded
        if (controller.isGrounded)
        {
            verticalVelocity = -0.5f; // small constant downward force to stay grounded
        }
        else
        {
            verticalVelocity -= gravity * Time.deltaTime;
        }

        // WASD Input
        float forwardInput = Input.GetAxis("Vertical");
        float sidewaysInput = Input.GetAxis("Horizontal");

        Vector3 speedMultiplier = new Vector3(sidewaysInput, 0, forwardInput);
        speedMultiplier = transform.TransformDirection(speedMultiplier);
        speedMultiplier *= walkSpeed;

        // Combine horizontal move speed and vertical gravity
        Vector3 finalVelocity = new Vector3(speedMultiplier.x, verticalVelocity, speedMultiplier.z);

        // Move Player
        controller.Move(finalVelocity * Time.deltaTime);
    }

    private void HandleFlashlightToggle()
    {
        if (Input.GetKeyDown(KeyCode.F))
        {
            if (flashlight != null)
            {
                flashlight.enabled = !flashlight.enabled;
            }
        }
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        // Physics push logic when character collides with rigidbodies (like doors)
        Rigidbody body = hit.collider.attachedRigidbody;
        if (body == null || body.isKinematic) return;

        // Avoid pushing objects below us
        if (hit.moveDirection.y < -0.3f) return;

        // Calculate push direction relative to movement hit
        Vector3 pushDir = new Vector3(hit.moveDirection.x, 0, hit.moveDirection.z);

        // Apply impulse at collision point
        body.AddForceAtPosition(pushDir * pushForce, hit.point, ForceMode.Impulse);
    }
}
