using UnityEngine;

public class PhysicalDoor : MonoBehaviour
{
    private Transform doorPanel;

    private void Start()
    {
        InitializeDoor();
    }

    private void InitializeDoor()
    {
        // 1. Find the door panel child recursively (contains "door" but not "handle", "frame", and not root name)
        string rootName = gameObject.name;
        if (rootName.EndsWith("(Clone)"))
        {
            rootName = rootName.Substring(0, rootName.Length - 7);
        }

        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child == transform) continue;
            string childName = child.name;
            string childNameLower = childName.ToLower();
            
            if (childNameLower.Contains("door") && 
                childName != rootName && 
                !childNameLower.Contains("handle") && 
                !childNameLower.Contains("frame"))
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

        // Force non-static status at runtime so physics work correctly
        doorPanel.gameObject.isStatic = false;
        foreach (Transform t in doorPanel.GetComponentsInChildren<Transform>(true))
        {
            t.gameObject.isStatic = false;
        }

        // 2. Ensure doorframe (root) has a kinematic Rigidbody
        Rigidbody frameRb = GetComponent<Rigidbody>();
        if (frameRb == null)
        {
            frameRb = gameObject.AddComponent<Rigidbody>();
        }
        frameRb.isKinematic = true;

        // 3. Ensure BoxCollider and Rigidbody exist on the door panel
        Collider doorCollider = doorPanel.GetComponent<BoxCollider>();
        if (doorCollider == null)
        {
            MeshCollider existingMeshCollider = doorPanel.GetComponent<MeshCollider>();
            if (existingMeshCollider != null)
            {
                DestroyImmediate(existingMeshCollider);
            }

            BoxCollider boxCol = doorPanel.gameObject.AddComponent<BoxCollider>();
            MeshFilter mf = doorPanel.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                boxCol.center = mf.sharedMesh.bounds.center;
                boxCol.size = mf.sharedMesh.bounds.size;
            }
            else
            {
                boxCol.center = new Vector3(0.49f, 1.06f, 0.04f);
                boxCol.size = new Vector3(1.01f, 2.07f, 0.09f);
            }
        }

        Rigidbody doorRb = doorPanel.GetComponent<Rigidbody>();
        if (doorRb == null)
        {
            doorRb = doorPanel.gameObject.AddComponent<Rigidbody>();
        }
        doorRb.mass = 15f;
        doorRb.drag = 1.0f;
        doorRb.angularDrag = 3.0f;
        doorRb.useGravity = true;

        // 4. Ensure HingeJoint exists on the door panel
        HingeJoint doorHinge = doorPanel.GetComponent<HingeJoint>();
        if (doorHinge == null)
        {
            doorHinge = doorPanel.gameObject.AddComponent<HingeJoint>();
        }
        doorHinge.connectedBody = frameRb;
        doorHinge.anchor = Vector3.zero;
        doorHinge.axis = Vector3.up;
        doorHinge.autoConfigureConnectedAnchor = true; // Prevents the hinge from stretching the joint space
        doorHinge.useLimits = true;
        JointLimits limits = new JointLimits();
        limits.min = -120f;
        limits.max = 120f;
        limits.bounciness = 0.1f;
        doorHinge.limits = limits;

        // 5. IGNORE COLLISIONS between door panel and nearby environment (doorframe, pillars, walls, floors, ceilings)
        // to prevent physics engine jittering and locking.
        Collider[] doorColliders = doorPanel.GetComponentsInChildren<Collider>(true);
        
        // Find all colliders within a 2.5m sphere at the doorway position
        Collider[] nearbyColliders = Physics.OverlapSphere(transform.position, 2.5f, ~0, QueryTriggerInteraction.Collide);

        foreach (var doorCol in doorColliders)
        {
            // First ignore internal doorway frame colliders
            Collider[] frameColliders = GetComponentsInChildren<Collider>(true);
            foreach (var frameCol in frameColliders)
            {
                if (frameCol != doorCol && !frameCol.transform.IsChildOf(doorPanel))
                {
                    Physics.IgnoreCollision(doorCol, frameCol, true);
                }
            }

            // Next ignore nearby environmental static and decorative colliders
            foreach (var col in nearbyColliders)
            {
                if (col == doorCol || col.transform.IsChildOf(doorPanel))
                    continue;

                string colNameLower = col.name.ToLower();
                bool isStaticEnv = col.gameObject.isStatic || 
                                   col.gameObject.layer == 8 || // Environment
                                   col.gameObject.layer == 12 || // Decoration
                                   colNameLower.Contains("pillar") || 
                                   colNameLower.Contains("wall") || 
                                   colNameLower.Contains("floor") || 
                                   colNameLower.Contains("ceiling") || 
                                   colNameLower.Contains("doorway") ||
                                   colNameLower.Contains("column") ||
                                   colNameLower.Contains("barrier");

                if (isStaticEnv)
                {
                    Physics.IgnoreCollision(doorCol, col, true);
                }
            }
        }
    }

    public void Interact()
    {
        // Default physical impulse when interacted without parameters
        if (doorPanel == null) return;
        Rigidbody rb = doorPanel.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.AddRelativeTorque(Vector3.up * 12f, ForceMode.Impulse);
            Debug.Log($"[PhysicalDoor] Applied default relative torque to {gameObject.name}");
        }
    }

    public void Interact(Vector3 pushDirection, Vector3 hitPoint)
    {
        if (doorPanel == null) return;
        Rigidbody rb = doorPanel.GetComponent<Rigidbody>();
        if (rb != null)
        {
            // Apply push force at the raycast hit point
            rb.AddForceAtPosition(pushDirection * 15.0f, hitPoint, ForceMode.Impulse);
            Debug.Log($"[PhysicalDoor] Applied direct force of {pushDirection * 15f} to {gameObject.name} at {hitPoint}");
        }
    }
}
