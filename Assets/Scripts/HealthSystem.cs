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

    [Header("只能被近战破坏（可打破箱子用）")]
    [Tooltip("勾上后：枪弹/爆炸等'非近战'伤害一律无效（可打破箱子靠它实现'只能用刀砸开'）")]
    public bool meleeOnly = false;
    [Tooltip("被非近战伤害打中时的'弹开'音效（可选，如枪打上去的'叮'）；不拖 = 静音")]
    public AudioClip meleeBlockClip;

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

    /// <summary>
    /// 扣血。fromMelee = 是否来自近战攻击（枪/爆炸等一律 false）。
    /// 若 meleeOnly = true，则只有近战伤害（fromMelee=true）才生效——可打破箱子"只能用刀砸开"。
    /// 加了默认参数，现有所有调用（枪/爆炸/敌人攻击/倒计时）都不用改，行为不变。
    /// </summary>
    public void TakeDamage(int damage, bool fromMelee = false)
    {
        // 只能被近战破坏：非近战伤害直接无效（可选播"弹开"音效）
        if (meleeOnly && !fromMelee)
        {
            if (meleeBlockClip != null)
                AudibleAudio.PlayAt(meleeBlockClip, transform.position);
            return;
        }

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
