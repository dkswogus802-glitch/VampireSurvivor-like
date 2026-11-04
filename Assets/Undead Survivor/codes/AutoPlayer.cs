using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// FSM(유한 상태 기계) 기반 자동 플레이 봇.
/// GameManager 오브젝트에 컴포넌트로 추가하면 동작한다.
///
/// - 이동: 주변 적의 위치를 보고 Wander / Kite / Flee 세 상태를 오간다.
/// - 아이템: 레벨업 창이 뜨면 설정한 전략(ItemStrategy)에 따라 자동 선택한다.
/// - 반복: 게임이 끝나면 자동으로 재시작해 targetRuns 판을 연속으로 플레이한다.
///   캐릭터와 아이템 전략을 판마다 돌아가며 바꿔서 조건별 비교 데이터를 만든다.
/// </summary>
public class AutoPlayer : MonoBehaviour
{
    public enum BotState { Wander, Kite, Flee }
    public enum ItemStrategy { Random, WeaponFirst, GearFirst }

    [Header("# Auto Play")]
    [Tooltip("체크를 끄면 봇이 꺼지고 평소처럼 직접 플레이한다.")]
    public bool autoPlay = true;
    [Tooltip("연속으로 플레이할 판 수")]
    public int targetRuns = 30;
    [Tooltip("게임 배속 (너무 높이면 물리 계산이 부정확해질 수 있음)")]
    [Range(1f, 8f)] public float simSpeed = 4f;
    [Tooltip("판마다 돌아가며 사용할 캐릭터 번호 (잠긴 캐릭터는 0번으로 대체)")]
    public int[] characterIds = { 0, 1 };
    [Tooltip("판마다 돌아가며 사용할 아이템 선택 전략")]
    public ItemStrategy[] strategies = { ItemStrategy.Random, ItemStrategy.WeaponFirst, ItemStrategy.GearFirst };

    [Header("# FSM")]
    [Tooltip("이 거리 안의 적을 인식한다")]
    public float senseRange = 6f;
    [Tooltip("가장 가까운 적이 이 거리보다 가까우면 도망(Flee)")]
    public float fleeRange = 1.8f;
    [Tooltip("체력이 이 비율 아래면 무조건 도망(Flee)")]
    [Range(0f, 1f)] public float lowHealthRatio = 0.3f;
    [Tooltip("Wander 상태에서 방향을 바꾸는 주기(초)")]
    public float wanderChangeTime = 2f;
    [Tooltip("Kite 상태에서 옆으로 도는 비율 (0 = 정반대로만 도망, 1 = 원을 그리며 돎)")]
    [Range(0f, 1f)] public float kiteTangent = 0.6f;

    [Header("# Debug (실행 중 확인용)")]
    public BotState state;
    public int runIndex;
    public ItemStrategy currentStrategy;

    // 현재 동작 중인 봇. 없으면 null (= 사람이 플레이)
    public static AutoPlayer Active { get; private set; }

    // GameManager.Resume에서 사용하는 배속. 봇이 없으면 1배속
    public static float TimeScale
    {
        get { return Active != null ? Active.simSpeed : 1f; }
    }

    // 씬을 다시 불러와도 유지되어야 하므로 static
    static int runsDone;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        runsDone = 0;
        Active = null;
    }

    readonly Collider2D[] hits = new Collider2D[128];
    Vector2 wanderDir;
    float wanderTimer;
    float orbitSign = 1f;
    bool endHandled;

    void OnEnable()
    {
        if (autoPlay && runsDone < targetRuns)
            Active = this;
    }

    void OnDisable()
    {
        if (Active == this)
            Active = null;
    }

    void Start()
    {
        if (Active != this)
            return;
        StartCoroutine(StartRunRoutine());
    }

    IEnumerator StartRunRoutine()
    {
        // AchiveManager가 캐릭터 잠금 상태를 먼저 정리하도록 잠깐 대기
        yield return new WaitForSecondsRealtime(0.3f);

        runIndex = runsDone;
        int charId = characterIds.Length > 0 ? characterIds[runIndex % characterIds.Length] : 0;
        // 캐릭터를 한 바퀴 다 돌 때마다 전략을 바꿈 → 캐릭터 x 전략 조합이 고르게 나옴
        int perStrategy = Mathf.Max(characterIds.Length, 1);
        currentStrategy = strategies.Length > 0
            ? strategies[(runIndex / perStrategy) % strategies.Length]
            : ItemStrategy.Random;

        Button startButton = FindCharacterButton(charId);
        if (startButton == null)
        {
            Debug.LogWarning("[AutoPlayer] 캐릭터 " + charId + "번이 잠겨 있어 0번으로 대체합니다.");
            startButton = FindCharacterButton(0);
        }
        if (startButton == null)
        {
            Debug.LogError("[AutoPlayer] 캐릭터 선택 버튼을 찾지 못했습니다.");
            yield break;
        }

        Debug.Log("[AutoPlayer] " + (runIndex + 1) + "/" + targetRuns + "판 시작 (전략: " + currentStrategy + ")");
        GameLogger.SetRunMeta("bot", currentStrategy.ToString(), simSpeed);
        // 실제 버튼 클릭과 똑같이 처리 (시작 화면 숨김 + HUD 표시 + GameStart)
        startButton.onClick.Invoke();
    }

    Button FindCharacterButton(int id)
    {
        string target = "charactor " + id;   // 씬의 버튼 이름 (원본 철자 그대로)
        foreach (Button b in FindObjectsOfType<Button>())
        {
            if (b.gameObject.name == target)
                return b;
        }
        return null;
    }

    void Update()
    {
        if (Active != this)
            return;

        GameManager gm = GameManager.instance;

        // 1. 게임이 끝나서 결과 화면이 뜨면 → 다음 판으로
        if (gm.uiResult.gameObject.activeSelf)
        {
            if (!endHandled)
            {
                endHandled = true;
                StartCoroutine(NextRunRoutine());
            }
            return;
        }

        // 2. 레벨업 창이 떠 있으면 → 아이템 선택
        if (gm.uiLevelUp.transform.localScale.x > 0.5f)
            PickItem();
    }

    IEnumerator NextRunRoutine()
    {
        yield return new WaitForSecondsRealtime(1f);
        runsDone++;

        if (runsDone < targetRuns)
        {
            GameManager.instance.GameRetry();   // 씬을 다시 불러오면 새 AutoPlayer가 다음 판을 시작
        }
        else
        {
            Debug.Log("[AutoPlayer] 목표 " + targetRuns + "판 완료! 로그 위치: " + GameLogger.LogDirectory);
            Active = null;
            enabled = false;
        }
    }

    // ---------------- 아이템 선택 ----------------

    void PickItem()
    {
        Item[] options = GameManager.instance.uiLevelUp.GetComponentsInChildren<Item>(false);
        if (options.Length == 0)
            return;

        Item choice = null;
        switch (currentStrategy)
        {
            case ItemStrategy.WeaponFirst:
                choice = FirstOfGroup(options, true);
                break;
            case ItemStrategy.GearFirst:
                choice = FirstOfGroup(options, false);
                break;
        }
        if (choice == null)
            choice = options[Random.Range(0, options.Length)];

        // 실제 버튼 클릭과 똑같이 처리 (Item.OnClick + LevelUp.Hide)
        choice.GetComponent<Button>().onClick.Invoke();
    }

    static Item FirstOfGroup(Item[] options, bool weapon)
    {
        foreach (Item item in options)
        {
            ItemData.ItemType t = item.data.itemType;
            bool isWeapon = t == ItemData.ItemType.Melee || t == ItemData.ItemType.Range;
            bool isGear = t == ItemData.ItemType.Glove || t == ItemData.ItemType.Shoe;
            if ((weapon && isWeapon) || (!weapon && isGear))
                return item;
        }
        return null;
    }

    // ---------------- 이동 (FSM) ----------------

    /// <summary>Player.Update에서 키보드 입력 대신 호출된다.</summary>
    public Vector2 GetMoveInput()
    {
        GameManager gm = GameManager.instance;
        Vector2 myPos = gm.player.transform.position;

        // 주변 적 탐색 → "적에게서 멀어지는 방향"을 가까운 적일수록 크게 반영
        int count = Physics2D.OverlapCircleNonAlloc(myPos, senseRange, hits, gm.player.scanner.targetLayer);
        Vector2 away = Vector2.zero;
        float nearest = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            Vector2 diff = myPos - (Vector2)hits[i].transform.position;
            float dist = diff.magnitude;
            if (dist < 0.01f)
                continue;
            nearest = Mathf.Min(nearest, dist);
            away += diff / (dist * dist);
        }

        // 상태 전이
        BotState next;
        if (count == 0 || away == Vector2.zero)
            next = BotState.Wander;
        else if (nearest < fleeRange || gm.health < gm.maxHealth * lowHealthRatio)
            next = BotState.Flee;
        else
            next = BotState.Kite;

        if (next != state)
        {
            if (next == BotState.Kite)
                orbitSign = Random.value < 0.5f ? -1f : 1f;   // 시계/반시계 방향 무작위
            state = next;
        }

        GameLogger.AddBotStateTime((int)state, Time.deltaTime);

        // 상태별 행동
        switch (state)
        {
            case BotState.Wander:
                wanderTimer -= Time.deltaTime;
                if (wanderTimer <= 0f)
                {
                    wanderDir = Random.insideUnitCircle.normalized;
                    wanderTimer = wanderChangeTime;
                }
                return wanderDir;

            case BotState.Flee:
                return away.normalized;

            default: // Kite: 적과 거리를 유지하며 원을 그리듯 돌아서 무기가 적을 맞히게 함
                Vector2 a = away.normalized;
                Vector2 tangent = new Vector2(-a.y, a.x) * orbitSign;
                return (a * (1f - kiteTangent) + tangent * kiteTangent).normalized;
        }
    }
}
