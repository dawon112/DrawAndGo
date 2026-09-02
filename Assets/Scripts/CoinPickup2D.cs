using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public sealed class CoinPickup2D : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponentInParent<PlayerController2D>() != null)
            gameObject.SetActive(false);
    }
}
