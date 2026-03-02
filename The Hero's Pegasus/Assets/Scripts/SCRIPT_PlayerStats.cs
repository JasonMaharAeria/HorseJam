using UnityEngine;

/// <summary>
/// Singleton that stores permanent stat multipliers accumulated via upgrade pickups.
/// Attach to any persistent scene GameObject (e.g., the GameManager or player root).
/// </summary>
public class SCRIPT_PlayerStats : MonoBehaviour
{
    public static SCRIPT_PlayerStats Instance { get; private set; }

    // ── Arrow stats ────────────────────────────────────────────────────────────

    /// <summary>
    /// Cumulative scale multiplier applied to arrows when they are fired.
    /// Each ArrowSizeIncrease upgrade multiplies this by 1.10.
    /// </summary>
    public float ArrowSizeMultiplier    { get; private set; } = 1f;

    /// <summary>
    /// Cumulative fire-rate multiplier. Each AttackSpeedIncrease upgrade multiplies by 1.10.
    /// Consumed by SCRIPT_Hero to scale its effective fire rate each frame.
    /// </summary>
    public float AttackSpeedMultiplier  { get; private set; } = 1f;

    /// <summary>
    /// Cumulative damage multiplier applied to each arrow on fire.
    /// Each ArrowDamageIncrease upgrade multiplies by 1.10.
    /// </summary>
    public float ArrowDamageMultiplier  { get; private set; } = 1f;

    /// <summary>
    /// Cumulative multiplier applied to the pegasus dash speed.
    /// Each DashSpeedIncrease upgrade multiplies by 1.10.
    /// </summary>
    public float DashSpeedMultiplier    { get; private set; } = 1f;

    /// <summary>
    /// Cumulative multiplier applied to the laser's particle start speed and start size.
    /// Each LaserUpgrade multiplies by 1.10.
    /// </summary>
    public float LaserMultiplier        { get; private set; } = 1f;

    /// <summary>
    /// Cumulative multiplier applied to arrow travel speed on fire.
    /// Each ArrowVelocityIncrease upgrade multiplies by 1.10.
    /// </summary>
    public float ArrowVelocityMultiplier { get; private set; } = 1f;

    // ── Regen stats ────────────────────────────────────────────────────────────

    /// <summary>
    /// Cumulative multiplier applied to SCRIPT_HealthBar.regenerationRate.
    /// Each HealthRegenIncrease upgrade multiplies by 1.10.
    /// </summary>
    public float HealthRegenMultiplier  { get; private set; } = 1f;

    /// <summary>
    /// Cumulative multiplier applied to the stamina regeneration rate.
    /// Each StaminaRegenIncrease upgrade multiplies by 1.10.
    /// </summary>
    public float StaminaRegenMultiplier { get; private set; } = 1f;

    /// <summary>
    /// Tracks the cumulative max-health multiplier for display purposes.
    /// The actual expansion is applied immediately to SCRIPT_PlayerMovementController.
    /// </summary>
    public float MaxHealthMultiplier  { get; private set; } = 1f;

    /// <summary>
    /// Tracks the cumulative max-stamina multiplier for display purposes.
    /// The actual expansion is applied immediately to SCRIPT_StaminaBar.
    /// </summary>
    public float MaxStaminaMultiplier { get; private set; } = 1f;

    // ──────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// Permanently applies the effect of an upgrade type to the relevant stat. 
    /// Called by SCRIPT_UpgradePickup when the player collects a pickup.
    /// </summary>
    public void ApplyUpgrade(UpgradeType type)
    {
        switch (type)
        {
            case UpgradeType.ArrowSizeIncrease:
            {
                float prev = ArrowSizeMultiplier;
                ArrowSizeMultiplier *= 1.15f;
                Debug.Log($"[PlayerStats] Arrow size ×{ArrowSizeMultiplier:F3}");
                SCRIPT_UpgradeNotification.Instance?.Show(
                    "Arrow Size",
                    "Arrows are now larger",
                    $"×{prev:F2}",
                    $"×{ArrowSizeMultiplier:F2}"
                );
                break;
            }
            case UpgradeType.AttackSpeedIncrease:
            {
                float prev = AttackSpeedMultiplier;
                AttackSpeedMultiplier *= 1.10f;
                Debug.Log($"[PlayerStats] Attack speed ×{AttackSpeedMultiplier:F3}");
                SCRIPT_UpgradeNotification.Instance?.Show(
                    "Attack Speed",
                    "Hero fires arrows faster",
                    $"×{prev:F2}",
                    $"×{AttackSpeedMultiplier:F2}"
                );
                break;
            }
            case UpgradeType.ArrowDamageIncrease:
            {
                float prev = ArrowDamageMultiplier;
                ArrowDamageMultiplier *= 1.10f;
                Debug.Log($"[PlayerStats] Arrow damage ×{ArrowDamageMultiplier:F3}");
                SCRIPT_UpgradeNotification.Instance?.Show(
                    "Arrow Damage",
                    "Arrows deal more damage",
                    $"×{prev:F2}",
                    $"×{ArrowDamageMultiplier:F2}"
                );
                break;
            }
            case UpgradeType.DashSpeedIncrease:
            {
                float prev = DashSpeedMultiplier;
                DashSpeedMultiplier *= 1.10f;
                Debug.Log($"[PlayerStats] Dash speed ×{DashSpeedMultiplier:F3}");
                SCRIPT_UpgradeNotification.Instance?.Show(
                    "Dash Speed",
                    "Pegasus dashes faster",
                    $"×{prev:F2}",
                    $"×{DashSpeedMultiplier:F2}"
                );
                break;
            }
            case UpgradeType.LaserUpgrade:
            {
                float prev = LaserMultiplier;
                LaserMultiplier *= 1.10f;
                Debug.Log($"[PlayerStats] Laser ×{LaserMultiplier:F3}");
                SCRIPT_UpgradeNotification.Instance?.Show(
                    "Laser Power",
                    "Laser particles are faster and larger",
                    $"×{prev:F2}",
                    $"×{LaserMultiplier:F2}"
                );
                break;
            }
            case UpgradeType.ArrowVelocityIncrease:
            {
                float prev = ArrowVelocityMultiplier;
                ArrowVelocityMultiplier *= 1.10f;
                Debug.Log($"[PlayerStats] Arrow velocity ×{ArrowVelocityMultiplier:F3}");
                SCRIPT_UpgradeNotification.Instance?.Show(
                    "Arrow Velocity",
                    "Arrows fly faster",
                    $"×{prev:F2}",
                    $"×{ArrowVelocityMultiplier:F2}"
                );
                break;
            }
            case UpgradeType.HealthRegenIncrease:
            {
                float prev = HealthRegenMultiplier;
                HealthRegenMultiplier *= 1.10f;
                Debug.Log($"[PlayerStats] Health regen ×{HealthRegenMultiplier:F3}");
                SCRIPT_UpgradeNotification.Instance?.Show(
                    "Health Regen",
                    "Pegasus heals faster",
                    $"×{prev:F2}",
                    $"×{HealthRegenMultiplier:F2}"
                );
                break;
            }
            case UpgradeType.StaminaRegenIncrease:
            {
                float prev = StaminaRegenMultiplier;
                StaminaRegenMultiplier *= 1.10f;
                Debug.Log($"[PlayerStats] Stamina regen ×{StaminaRegenMultiplier:F3}");
                SCRIPT_UpgradeNotification.Instance?.Show(
                    "Stamina Regen",
                    "Dash recovers faster",
                    $"×{prev:F2}",
                    $"×{StaminaRegenMultiplier:F2}"
                );
                break;
            }
            case UpgradeType.MaxHealthIncrease:
            {
                float prev = MaxHealthMultiplier;
                MaxHealthMultiplier *= 1.10f;
                var player = FindFirstObjectByType<SCRIPT_PlayerMovementController>();
                if (player != null) player.IncreaseMaxHealth(1.10f);
                Debug.Log($"[PlayerStats] Max health ×{MaxHealthMultiplier:F3}");
                SCRIPT_UpgradeNotification.Instance?.Show(
                    "Max Health",
                    "Pegasus has more health",
                    $"×{prev:F2}",
                    $"×{MaxHealthMultiplier:F2}"
                );
                break;
            }
            case UpgradeType.MaxStaminaIncrease:
            {
                float prev = MaxStaminaMultiplier;
                MaxStaminaMultiplier *= 1.10f;
                var bar = FindFirstObjectByType<SCRIPT_StaminaBar>();
                if (bar != null) bar.IncreaseMaxStamina(1.10f);
                Debug.Log($"[PlayerStats] Max stamina ×{MaxStaminaMultiplier:F3}");
                SCRIPT_UpgradeNotification.Instance?.Show(
                    "Max Stamina",
                    "Longer dash duration",
                    $"×{prev:F2}",
                    $"×{MaxStaminaMultiplier:F2}"
                );
                break;
            }
        }
    }
}
