using UnityEngine;
using UnityEngine.UI;

public class SCRIPT_StaminaBar : MonoBehaviour
{

    // Stamina points. Drains when dashing, regenerates when not dashing, and dash goes into cooldown when it reaches 0.
    public float secondsOfStamina = 5f; // How many seconds to take the bar from full to empty when dashing (for balancing purposes)
    public float secondsToRefreshStamina = 10f; // How many seconds to take the bar from empty to full when not dashing (for balancing purposes)

    private float maxStamina = 100f;
    public float currentStamina;
    public bool isOnCooldown = false;   // state tracking

    public Slider staminaSlider;
    public Image fillImage;

    public Color normalColor = Color.yellow;
    public Color exhaustedColor = Color.red;


    void Start()
    {
        currentStamina = maxStamina;
        staminaSlider.maxValue = maxStamina;
        staminaSlider.value = currentStamina; 
        fillImage.color = normalColor;
    }


    public void UseStamina()
    {
        currentStamina -= maxStamina / secondsOfStamina * Time.fixedDeltaTime;
        if (currentStamina < 0)
        {
            currentStamina = 0;

            // Out of stamina, trigger cooldown
            isOnCooldown = true;
            fillImage.color = exhaustedColor; // Turn red when exhausted
        }

        staminaSlider.value = currentStamina;
    }

    public void RegenStamina()
    {
        // Simple regeneration logic
        if (currentStamina < maxStamina)
        {
            currentStamina += maxStamina / secondsToRefreshStamina * Time.deltaTime;
        }
        else
        {
            isOnCooldown = false;
            currentStamina = maxStamina;
            fillImage.color = normalColor; // Reset color to green
        }

        staminaSlider.value = currentStamina;
    }

    public bool CanDash()
    {
        return !isOnCooldown;
    }

    public bool IsFull()
    {
        return currentStamina == maxStamina;
    }

    /// <summary>Stamina as a 0–1 fraction. Used by SCRIPT_PlayerAudio to modulate dash pitch.</summary>
    public float StaminaFraction => currentStamina / maxStamina;

}