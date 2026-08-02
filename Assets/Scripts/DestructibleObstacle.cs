using UnityEngine;
using UnityEngine.Events;

public class DestructibleObstacle : MonoBehaviour
{
    [Header("血量设置")]
    public int maxHealth = 5;
    private int currentHealth;

    [Header("音效")]
    public AudioSource hitSound;
    public AudioSource destroySound;

    [Header("特效")]
    public GameObject destroyEffect;

    [Header("被摧毁后")]
    public UnityEvent onDestroyed;
    public bool disableOnDestroy = true;

    void Start()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(int damage)
    {
        if (currentHealth <= 0) return;

        currentHealth -= damage;
        if (hitSound != null) hitSound.Play();

        if (currentHealth <= 0)
            DestroyMe();
    }

    private void DestroyMe()
    {
        if (destroySound != null) destroySound.Play();
        if (destroyEffect != null)
            Instantiate(destroyEffect, transform.position, Quaternion.identity);

        onDestroyed?.Invoke();

        if (disableOnDestroy)
            gameObject.SetActive(false);
    }

    public int GetHealth() => currentHealth;
    public int GetMaxHealth() => maxHealth;
}
