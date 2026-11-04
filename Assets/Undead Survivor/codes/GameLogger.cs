using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 플레이 데이터를 CSV 파일로 기록하는 정적 로거.
/// 씬에 붙일 필요 없이 GameManager, Item, Enemy에서 호출만 하면 동작한다.
///
/// 저장 위치: Application.persistentDataPath/PlayLogs/
///   - runs.csv      : 한 판당 1행 (결과 요약)
///   - events.csv    : 레벨업, 아이템 선택, 처치, 사망 등 이벤트 단위 기록
///   - snapshots.csv : 5초마다 상태 기록 (레벨, 처치 수, 체력, 화면 내 적 수)
/// </summary>
public static class GameLogger
{
    const float SnapshotInterval = 5f;
    const int EnemyPoolIndex = 0;

    const string RunsHeader =
        "run_id,started_at,character_id,result,survival_time,max_game_time,final_level,total_kills,final_health,max_health," +
        "controller,item_strategy,sim_speed,bot_wander_time,bot_kite_time,bot_flee_time,balance_version";
    const string EventsHeader =
        "run_id,game_time,event_type,detail_id,detail_name,value,level,kills,health";
    const string SnapshotsHeader =
        "run_id,game_time,level,kills,health,active_enemies";

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    // BOM 포함 UTF-8: 엑셀에서 열어도 한글이 깨지지 않는다.
    static readonly Encoding Utf8Bom = new UTF8Encoding(true);

    static readonly StringBuilder eventBuffer = new StringBuilder();
    static readonly StringBuilder snapshotBuffer = new StringBuilder();

    static string runId;
    static DateTime startedAt;
    static int characterId;
    static float nextSnapshotTime;
    static bool quitHooked;

    // 판 정보 (봇이 SetRunMeta로 설정, 사람이 플레이하면 기본값)
    static string controller = "human";
    static string itemStrategy = "-";
    static float simSpeed = 1f;
    static readonly float[] botStateTime = new float[3];   // Wander, Kite, Flee 상태로 보낸 시간

    public static bool IsRecording { get; private set; }

    public static string LogDirectory
    {
        get { return Path.Combine(Application.persistentDataPath, "PlayLogs"); }
    }

    /// <summary>StartRun 전에 호출. 누가(사람/봇) 어떤 전략으로 플레이하는지 기록한다.</summary>
    public static void SetRunMeta(string controllerName, string strategy, float speed)
    {
        controller = controllerName;
        itemStrategy = strategy;
        simSpeed = speed;
    }

    /// <summary>봇이 각 상태(0=Wander, 1=Kite, 2=Flee)로 보낸 시간을 누적한다.</summary>
    public static void AddBotStateTime(int state, float dt)
    {
        if (IsRecording && state >= 0 && state < botStateTime.Length)
            botStateTime[state] += dt;
    }

    /// <summary>게임 시작 시 호출. 새 판의 기록을 시작한다.</summary>
    public static void StartRun(int playerId)
    {
        if (IsRecording)
        {
            // 이전 판이 끝나지 않은 채 새 판이 시작된 경우: 이전 판은 abandoned로 저장하고
            // 이번 판의 정보(SetRunMeta로 설정한 값)는 그대로 유지
            string c = controller, s = itemStrategy;
            float sp = simSpeed;
            EndRun("abandoned");
            SetRunMeta(c, s, sp);
        }

        runId = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", Inv);
        startedAt = DateTime.Now;
        characterId = playerId;
        nextSnapshotTime = 0f;
        eventBuffer.Length = 0;
        snapshotBuffer.Length = 0;
        for (int i = 0; i < botStateTime.Length; i++)
            botStateTime[i] = 0f;
        IsRecording = true;

        if (!quitHooked)
        {
            // 플레이 도중 게임을 끄면 그때까지의 기록을 "quit"으로 저장
            Application.quitting += () => { if (IsRecording) EndRun("quit"); };
            quitHooked = true;
        }

        Debug.Log("[GameLogger] 기록 시작: " + runId + " → " + LogDirectory);
    }

    /// <summary>이벤트 1건 기록. detailId/detailName은 아이템·적 종류 등.</summary>
    public static void LogEvent(string eventType, int detailId = -1, string detailName = "", float value = 0f)
    {
        if (!IsRecording)
            return;

        GameManager gm = GameManager.instance;
        eventBuffer.Append(runId).Append(',')
            .Append(F(gm.gameTime)).Append(',')
            .Append(eventType).Append(',')
            .Append(detailId).Append(',')
            .Append(Clean(detailName)).Append(',')
            .Append(F(value)).Append(',')
            .Append(gm.level).Append(',')
            .Append(gm.kill).Append(',')
            .Append(F(gm.health)).Append('\n');
    }

    /// <summary>GameManager.Update에서 매 프레임 호출. 일정 간격으로 스냅샷을 남긴다.</summary>
    public static void Tick()
    {
        if (!IsRecording)
            return;

        GameManager gm = GameManager.instance;
        if (gm.gameTime < nextSnapshotTime)
            return;

        nextSnapshotTime += SnapshotInterval;
        snapshotBuffer.Append(runId).Append(',')
            .Append(F(gm.gameTime)).Append(',')
            .Append(gm.level).Append(',')
            .Append(gm.kill).Append(',')
            .Append(F(gm.health)).Append(',')
            .Append(gm.pool.CountActive(EnemyPoolIndex)).Append('\n');
    }

    /// <summary>게임 종료(승리/패배) 시 호출. 버퍼를 파일에 저장한다.</summary>
    public static void EndRun(string result)
    {
        if (!IsRecording)
            return;

        GameManager gm = GameManager.instance;
        IsRecording = false;
        if (gm == null)
            return;

        string runLine = string.Join(",", new string[] {
            runId,
            startedAt.ToString("yyyy-MM-dd HH:mm:ss", Inv),
            characterId.ToString(Inv),
            result,
            F(gm.gameTime),
            F(gm.maxGameTime),
            gm.level.ToString(Inv),
            gm.kill.ToString(Inv),
            F(Mathf.Max(gm.health, 0f)),
            F(gm.maxHealth),
            controller,
            itemStrategy,
            F(simSpeed),
            F(botStateTime[0]),
            F(botStateTime[1]),
            F(botStateTime[2]),
            Clean(gm.balanceVersion)
        }) + "\n";

        // 다음 판을 위해 기본값(사람 플레이)으로 되돌림
        controller = "human";
        itemStrategy = "-";
        simSpeed = 1f;

        try
        {
            Directory.CreateDirectory(LogDirectory);
            Append("runs.csv", RunsHeader, runLine);
            Append("events.csv", EventsHeader, eventBuffer.ToString());
            Append("snapshots.csv", SnapshotsHeader, snapshotBuffer.ToString());
            Debug.Log("[GameLogger] 저장 완료: " + runId + " (" + result + ")");
        }
        catch (Exception e)
        {
            Debug.LogError("[GameLogger] 저장 실패: " + e.Message);
        }
    }

    static void Append(string fileName, string header, string body)
    {
        string path = Path.Combine(LogDirectory, fileName);
        if (!File.Exists(path))
            File.WriteAllText(path, header + "\n", Utf8Bom);
        if (body.Length > 0)
            File.AppendAllText(path, body, Utf8Bom);
    }

    static string F(float v)
    {
        return v.ToString("0.###", Inv);
    }

    // CSV를 깨뜨리는 문자 제거
    static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        return s.Replace(",", " ").Replace("\n", " ").Replace("\r", " ");
    }
}
