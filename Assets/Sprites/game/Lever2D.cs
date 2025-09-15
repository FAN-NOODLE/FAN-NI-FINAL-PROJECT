using UnityEngine;
using System;

[RequireComponent(typeof(Collider2D))]
public class Lever2D : MonoBehaviour
{
    public int leverId;
    [SerializeField] bool isOn;
    public GameObject prompt;                 // 世界空间提示
    public KeyCode interactKey = KeyCode.E;

    public event Action<int, bool> OnLeverToggled;

    bool playerInRange;
    Animator animator;
    static readonly int IsOnHash = Animator.StringToHash("IsOn");

    void Awake()
    {
        // 确保是触发器
        var col = GetComponent<Collider2D>();
        col.isTrigger = true;
        // 开局隐藏提示
        if (prompt) prompt.SetActive(false);
    }

    void Start()
    {
        animator = GetComponent<Animator>();
        UpdateVisual();
    }

    void OnDisable()
    {
        playerInRange = false;
        if (prompt) prompt.SetActive(false);
    }

    void Update()
    {
        if (playerInRange && Input.GetKeyDown(interactKey))
            Toggle();
    }

    public void Toggle()
    {
        SetState(!isOn);
    }

    /// <summary>
    /// 直接设置 Lever 状态为 ON/OFF（不会盲目翻转）
    /// </summary>
    public void SetState(bool on)
    {
        if (isOn == on) return; // 状态未变，不处理

        isOn = on;
        UpdateVisual();
        OnLeverToggled?.Invoke(leverId, isOn);

        Debug.Log($"[Lever2D] Lever {leverId} set to {(isOn ? "ON" : "OFF")}.");
    }

    void UpdateVisual()
    {
        if (animator) animator.SetBool(IsOnHash, isOn);
        // 这里不要改 prompt 的显隐，由触发器回调控制
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (IsPlayer(other))
        {
            playerInRange = true;
            if (prompt) prompt.SetActive(true);
        }
    }

    void OnTriggerStay2D(Collider2D other)
    {
        if (IsPlayer(other))
        {
            if (!playerInRange)
            {
                playerInRange = true;
                if (prompt) prompt.SetActive(true);
            }
        }
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (IsPlayer(other))
        {
            playerInRange = false;
            if (prompt) prompt.SetActive(false);
        }
    }

    bool IsPlayer(Collider2D other)
    {
        return other.GetComponentInParent<PlayerController2D>() != null;
    }

    public bool IsOn => isOn;
}

