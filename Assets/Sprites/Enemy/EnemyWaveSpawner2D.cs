using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class EnemyWaveSpawner2D : MonoBehaviour
{
    [System.Serializable]
    public class Wave
    {
        [Tooltip("可选名字，仅便于识别")]
        public string name = "Wave 1";

        [Tooltip("本波要刷出的数量")]
        public int count = 3;

        [Tooltip("同一波内，敌人与敌人之间的间隔秒数")]
        public float spawnInterval = 1f;

        [Tooltip("本波开始前的额外延迟（叠加在全局的 timeBetweenWaves 之前/之后）")]
        public float startDelay = 0f;

        [Tooltip("本波可用的敌人预制体（随机一个）；为空则用默认 enemyPrefab")]
        public GameObject[] enemyPrefabs;
    }

    [Header("基本")]
    public GameObject enemyPrefab;         // 默认敌人预制体（Wave.enemyPrefabs 为空时用它）
    public Transform[] spawnPoints;        // 刷新点；留空则用本物体位置
    public List<Wave> waves = new List<Wave>();

    [Header("节奏控制")]
    public int maxAlive = 3;               // 同时在场上限
    public float timeBetweenWaves = 2f;    // 两波之间的间隔
    public bool waitAliveToClear = false;  // 开启：下一波开始前必须清空场上残留
    public bool loopWaves = false;         // 轮完继续从第1波循环

    [Header("激活控制（可选）")]
    public Transform player;               // 不填会自动找
    public float activateRadius = 999f;    // 玩家进入此半径才会激活；给999相当于一直激活
    public bool startOnAwake = true;       // 场景开始就运行

    readonly List<GameObject> live = new List<GameObject>();
    bool running;
    int spawnIndex; // 用于轮询多个 spawnPoints

    void Start()
    {
        if (!player)
        {
            var pc = FindObjectOfType<PlayerController2D>();
            if (pc) player = pc.transform;
        }

        if (startOnAwake) Begin();
    }

    void Update()
    {
        // 只用于可视化/暂停逻辑：超出激活半径时，协程会自动“等着”
    }

    public void Begin()
    {
        if (running) return;
        running = true;
        StartCoroutine(RunWaves());
    }

    public void StopAll()
    {
        running = false;
        StopAllCoroutines();
    }

    IEnumerator RunWaves()
    {
        if (waves == null || waves.Count == 0)
        {
            Debug.LogWarning($"{name}: waves 为空，停止。");
            yield break;
        }

        do
        {
            for (int i = 0; i < waves.Count; i++)
            {
                Wave w = waves[i];

                // 玩家距离判定：等到进入激活半径
                yield return StartCoroutine(WaitUntilActive());

                // 波前延迟
                if (w.startDelay > 0f) yield return new WaitForSeconds(w.startDelay);

                int spawnedThisWave = 0;

                while (spawnedThisWave < w.count)
                {
                    // 玩家必须在激活半径内
                    yield return StartCoroutine(WaitUntilActive());

                    // 等待直到场上数量低于上限
                    while (live.Count >= maxAlive)
                        yield return null;

                    SpawnOne(w);
                    spawnedThisWave++;

                    // 同一波内的间隔
                    if (w.spawnInterval > 0f)
                        yield return new WaitForSeconds(w.spawnInterval);
                    else
                        yield return null;
                }

                // 两波之间：是否要求清场
                if (waitAliveToClear)
                    yield return StartCoroutine(WaitUntilCleared());

                // 两波之间的固定间隔
                if (timeBetweenWaves > 0f)
                    yield return new WaitForSeconds(timeBetweenWaves);
            }
        }
        while (loopWaves && running);
    }

    void SpawnOne(Wave w)
    {
        if (!enemyPrefab && (w.enemyPrefabs == null || w.enemyPrefabs.Length == 0))
        {
            Debug.LogError($"{name}: 没有可用的敌人预制体。");
            return;
        }

        // 选预制体
        GameObject prefab = enemyPrefab;
        if (w.enemyPrefabs != null && w.enemyPrefabs.Length > 0)
        {
            int r = Random.Range(0, w.enemyPrefabs.Length);
            if (w.enemyPrefabs[r]) prefab = w.enemyPrefabs[r];
        }

        // 选刷新点
        Vector3 pos = transform.position;
        Quaternion rot = Quaternion.identity;

        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            var sp = spawnPoints[spawnIndex % spawnPoints.Length];
            spawnIndex++;
            if (sp) { pos = sp.position; rot = sp.rotation; }
        }

        var go = Instantiate(prefab, pos, rot);
        live.Add(go);

        // 监听死亡，自动从列表移除
        var eh = go.GetComponent<EnemyHealth>();
        if (eh)
        {
            eh.OnDied += () => OnEnemyDied(go);
        }
        else
        {
            // 兜底：如果你忘了挂 EnemyHealth，尝试在销毁时移除
            StartCoroutine(RemoveWhenDestroyed(go));
        }
    }

    IEnumerator RemoveWhenDestroyed(GameObject go)
    {
        yield return new WaitUntil(() => go == null);
        live.Remove(go);
    }

    void OnEnemyDied(GameObject go)
    {
        live.Remove(go);
    }

    IEnumerator WaitUntilActive()
    {
        if (!player) yield break; // 没玩家就当始终激活

        while (true)
        {
            float dist = Vector2.Distance(player.position, transform.position);
            if (dist <= activateRadius) break;
            yield return null;
        }
    }

    IEnumerator WaitUntilCleared()
    {
        while (live.Count > 0) yield return null;
    }

    void OnDrawGizmosSelected()
    {
        // 激活半径
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, activateRadius);

        // 刷新点
        if (spawnPoints != null)
        {
            Gizmos.color = Color.green;
            foreach (var sp in spawnPoints)
            {
                if (!sp) continue;
                Gizmos.DrawSphere(sp.position, 0.08f);
                Gizmos.DrawLine(transform.position, sp.position);
            }
        }
        else
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(transform.position, 0.08f);
        }
    }
}


