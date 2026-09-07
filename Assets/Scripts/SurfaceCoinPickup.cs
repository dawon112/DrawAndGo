using UnityEngine;

// Same disable-on-contact contract as CoinPickup2D, for the surface player's 3D collider.
[RequireComponent(typeof(Collider))]
public sealed class SurfaceCoinPickup : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<DuduSurfaceMovement>() != null)
            gameObject.SetActive(false);
    }
}
