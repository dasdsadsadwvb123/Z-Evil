using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class HealthSystem : MonoBehaviour
{
    [Header("血量设置")]
    public int maxHealth = 3;
    public int currentHealth;

    [Header("受伤设置")]
    public float invincibleDuration = 0.5f;

    [Header("死亡设置")]
    public string deathTriggerName = "Death";
    public bool lockMovementOnDeath = true;
    public bool destroyOnDeath = false;
    public float deathAnimationTime = 1.5f;
    public bool reloadSceneOnDeath = false;

    private bool isInvincible = false;
    [HideInInspector] public bool isDead = false;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private PixelGridMovement playerMovement;

    public System.Action OnDamaged;
    public System.Action OnDeath;

    private void Start()
    {
        currentHealth = maxHealth;
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        playerMovement = GetComponent<PixelGridMovement>();
    }

    public void TakeDamage(int damage)
    {
        if (isDead || isInvincible) return;

        currentHealth -= damage;
        OnDamaged?.Invoke();

        if (currentHealth <= 0)
        {
            Die();
        }
        else
        {
            StartCoroutine(InvincibleFrames());
        }
    }

    /// <summary> 恢复生命（草药使用/其他回血手段调用）。不会超过最大血量 </summary>
    public void Heal(int amount)
    {
        if (isDead || amount <= 0) return;

        int before = currentHealth;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        int gained = currentHealth - before;

        if (gained > 0)
        {
            Debug.Log("[血量] 恢复 " + gained + " 点 → " + currentHealth + "/" + maxHealth);
        }
        else
        {
            Debug.Log("[血量] 生命值已满，没有恢复！");
        }
    }

    private IEnumerator InvincibleFrames()
    {
        isInvincible = true;

        if (spriteRenderer != null)
        {
            float endTime = Time.time + invincibleDuration;
            while (Time.time < endTime)
            {
                spriteRenderer.enabled = !spriteRenderer.enabled;
                yield return new WaitForSeconds(0.1f);
            }
            spriteRenderer.enabled = true;
        }
        else
        {
            yield return new WaitForSeconds(invincibleDuration);
        }

        isInvincible = false;
    }

    private void Die()
    {
        if (isDead) return;
        isDead = true;

        OnDeath?.Invoke();

        if (animator != null && !string.IsNullOrEmpty(deathTriggerName))
            animator.SetTrigger(deathTriggerName);

        if (lockMovementOnDeath && playerMovement != null)
            playerMovement.enabled = false;

        if (reloadSceneOnDeath)
            StartCoroutine(DeathReload());
        else if (destroyOnDeath)
            Destroy(gameObject, deathAnimationTime);
    }

    private IEnumerator DeathReload()
    {
        yield return new WaitForSeconds(deathAnimationTime);
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
