using UnityEngine;
using UnityEngine.UI;

public class SCRIPT_HealthBar : MonoBehaviour
{
    public bool regenerateHealth = true; // Whether the health should regenerate over time
    public float regenerationRate = 2f; // Amount of health regenerated per second

    [SerializeField] private Slider slider;
    [SerializeField] private Gradient gradient;
    [SerializeField] private Image fillImage;


    void Update()
    {
        // Handle regeneration
        if (regenerateHealth && slider.value < slider.maxValue)
        {
            AddHealth(regenerationRate * Time.deltaTime);
        }
    }

    // Initialize the bar with max health
    public void SetMaxHealth(float health)
    {
        slider.maxValue = health;
        slider.value = health;

        // Sets the bar color to the far right of the gradient (usually green)
        fillImage.color = gradient.Evaluate(1f);
    }

    // Update the bar and the color dynamically
    public void SetHealth(float health)
    {
        slider.value = health;

        // Evaluate the gradient based on the percentage (0 to 1)
        fillImage.color = gradient.Evaluate(slider.normalizedValue);
    }

    public void AddHealth(float amount)
    {
        slider.value += amount;
        if (slider.value > slider.maxValue)
        {
            slider.value = slider.maxValue;
        }
        else if (slider.value < 0)
        {
            slider.value = slider.minValue;
        }

        fillImage.color = gradient.Evaluate(slider.normalizedValue);
    }

    public void SetRegeneration(bool turnOn)
    {
        regenerateHealth = turnOn;
    }

    public bool IsRegenerating()
    {
        return regenerateHealth;
    }

}