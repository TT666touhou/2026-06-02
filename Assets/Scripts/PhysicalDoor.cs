using UnityEngine;

public class PhysicalDoor : MonoBehaviour
{
    [Header("Door Physics Settings")]
    public float doorMass = 15f;
    public float doorDrag = 1f;
    public float doorAngularDrag = 2f;

    [Header("Swing Limits")]
    public float minAngle = -90f;
    public float maxAngle = 90f;
    public float bounciness = 0.2f;

    private Transform doorPanel;
    private Rigidbody doorRigidbody;
    private HingeJoint doorHinge;
    private BoxCollider doorCollider;

    private void Start()
    {
        InitializeDoorPhysics();
    }

    private void InitializeDoorPhysics()
    {
        // 1. Find the door panel child (contains "door" but not "handle" to avoid selecting the handle first)
        foreach (Transform child in transform)
        {
            string lowerName = child.name.ToLower();
            if (lowerName.Contains("door") && !lowerName.Contains("handle"))
            {
                doorPanel = child;
                break;
            }
        }

        if (doorPanel == null)
        {
            Debug.LogWarning($"[PhysicalDoor] Could not find door panel child in {gameObject.name}!");
            return;
        }

        // 2. Add Rigidbody to the doorway frame (parent) as kinematic to act as the joint's fixed anchor
        Rigidbody frameRb = GetComponent<Rigidbody>();
        if (frameRb == null)
        {
            frameRb = gameObject.AddComponent<Rigidbody>();
            frameRb.isKinematic = true;
            frameRb.useGravity = false;
        }

        // 3. Add BoxCollider to the door panel (covers the door mesh properly)
        doorCollider = doorPanel.GetComponent<BoxCollider>();
        if (doorCollider == null)
        {
            // Remove any existing MeshCollider on the door to avoid duplicate collision calculation
            MeshCollider existingMeshCollider = doorPanel.GetComponent<MeshCollider>();
            if (existingMeshCollider != null)
            {
                Destroy(existingMeshCollider);
            }

            doorCollider = doorPanel.gameObject.AddComponent<BoxCollider>();
            
            // Set bounds according to MeshFilter bounds (which matches door_7 size: ~1.01m x 2.07m x 0.09m)
            MeshFilter mf = doorPanel.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                doorCollider.center = mf.sharedMesh.bounds.center;
                doorCollider.size = mf.sharedMesh.bounds.size;
            }
            else
            {
                // Fallback hardcoded values based on dump structure
                doorCollider.center = new Vector3(0.49f, 1.06f, 0.04f);
                doorCollider.size = new Vector3(1.01f, 2.07f, 0.09f);
            }
        }

        // 4. Add Rigidbody to the door panel
        doorRigidbody = doorPanel.GetComponent<Rigidbody>();
        if (doorRigidbody == null)
        {
            doorRigidbody = doorPanel.gameObject.AddComponent<Rigidbody>();
        }
        doorRigidbody.mass = doorMass;
        doorRigidbody.drag = doorDrag;
        doorRigidbody.angularDrag = doorAngularDrag;
        doorRigidbody.useGravity = true;
        doorRigidbody.isKinematic = false;
        doorRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        // 5. Add HingeJoint to the door panel (anchor at (0,0,0) which is already the hinge axis)
        doorHinge = doorPanel.GetComponent<HingeJoint>();
        if (doorHinge == null)
        {
            doorHinge = doorPanel.gameObject.AddComponent<HingeJoint>();
        }

        doorHinge.anchor = Vector3.zero;
        doorHinge.axis = new Vector3(0, 1, 0); // Rotate around Y axis
        doorHinge.connectedBody = frameRb;
        doorHinge.enableCollision = false; // Disable collision between door panel and doorframe rb

        // 6. Set Hinge limits
        doorHinge.useLimits = true;
        JointLimits limits = new JointLimits();
        limits.min = minAngle;
        limits.max = maxAngle;
        limits.bounciness = bounciness;
        doorHinge.limits = limits;

        // 7. Explicitly ignore collision between door collider and frame collider for perfect stability
        Collider frameCollider = GetComponent<Collider>();
        if (doorCollider != null && frameCollider != null)
        {
            Physics.IgnoreCollision(doorCollider, frameCollider);
        }
    }
}
