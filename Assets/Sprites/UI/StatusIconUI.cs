using UnityEngine;
using UnityEngine.UI;

public class StatusIconUI : MonoBehaviour
{
    [HideInInspector] public string id;
    [SerializeField] Image iconImage;
    [SerializeField] Image cooldownMask; // Radial Filled

    float endTime = -1f;
    float duration = 0f;
    bool ticking = false;

    public void Setup(string id, Sprite icon, float duration)
    {
        this.id = id;
        if (iconImage) iconImage.sprite = icon;
        this.duration = duration;
        if (duration > 0f)
        {
            endTime = Time.time + duration;
            ticking = true;
            if (cooldownMask) cooldownMask.fillAmount = 1f;
        }
        else
        {
            ticking = false;
            if (cooldownMask) cooldownMask.fillAmount = 0f;
        }
    }

    public void RefreshDuration(float duration)
    {
        Setup(id, iconImage ? iconImage.sprite : null, duration);
    }

    void Update()
    {
        if (!ticking || duration <= 0f) return;
        float remain = Mathf.Max(0f, endTime - Time.time);
        float ratio = Mathf.Clamp01(remain / duration);
        if (cooldownMask) cooldownMask.fillAmount = ratio;
        if (remain <= 0f) Destroy(gameObject);
    }
}

