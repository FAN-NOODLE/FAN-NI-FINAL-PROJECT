using UnityEngine;
public class UIPulse : MonoBehaviour
{
    public float speed = 4f;
    public float scaleMin = 0.95f, scaleMax = 1.05f;
    Vector3 baseScale;
    void Awake(){ baseScale = transform.localScale; }
    void Update(){
        float t = 0.5f * (Mathf.Sin(Time.time * speed) + 1f);
        transform.localScale = baseScale * Mathf.Lerp(scaleMin, scaleMax, t);
    }
}

