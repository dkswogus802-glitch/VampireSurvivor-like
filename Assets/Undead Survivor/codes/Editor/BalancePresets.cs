using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 밸런스 값을 코드로 관리하는 에디터 메뉴.
/// Unity 상단 메뉴 Tools > Balance 에서 버전을 골라 한 번에 적용한다.
/// Inspector에서 값을 하나씩 손으로 바꾸지 않아도 되고, 어떤 버전이 어떤 값이었는지 코드에 기록이 남는다.
///
/// 적용 후 반드시 Ctrl + S 로 씬을 저장할 것.
/// </summary>
public static class BalancePresets
{
    // ---------------- v1: 원본 (분석 결과 문제가 발견된 버전) ----------------
    // - 10초에 스폰 단계가 1번 → 2번으로 바뀐 뒤 더 이상 오르지 않음
    // - 필요 경험치 1~10 → 레벨 10 이후 10킬마다 레벨업 (5분에 레벨 약 130)
    [MenuItem("Tools/Balance/Apply v1 (원본)")]
    static void ApplyV1()
    {
        Apply("v1", 10f,
            new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
            new SpawnData[] {
                SD(0, 0.7f, 10, 2f),
                SD(1, 0.2f, 20, 2.4f),
            });
    }

    // ---------------- v2: 완만한 성장 곡선 ----------------
    // - 스폰 단계를 5개로 늘리고 60초마다 한 단계씩 어려워지게 함
    //   (원본의 10초 급경사 → 5분에 걸친 완만한 상승)
    // - 필요 경험치를 점점 늘려서 5분에 레벨 20 안팎, 무기·장비가 후반에 만렙이 되도록 함
    [MenuItem("Tools/Balance/Apply v2 (완만한 성장 곡선)")]
    static void ApplyV2()
    {
        Apply("v2", 60f,
            new int[] { 3, 5, 8, 12, 16, 20, 25, 30, 35, 40, 45, 50, 60, 70, 80 },
            new SpawnData[] {
                SD(0, 0.70f, 10, 2.0f),   //   0 ~  60초
                SD(0, 0.45f, 15, 2.2f),   //  60 ~ 120초
                SD(1, 0.30f, 20, 2.4f),   // 120 ~ 180초 (원본 2단계와 비슷한 적)
                SD(1, 0.22f, 30, 2.5f),   // 180 ~ 240초
                SD(1, 0.18f, 35, 2.5f),   // 240 ~ 300초
            });
    }

    static SpawnData SD(int spriteType, float spawnTime, int health, float speed)
    {
        SpawnData d = new SpawnData();
        d.spriteType = spriteType;   // 프리팹이 0, 1번만 지원하므로 0 또는 1만 사용
        d.spawnTime = spawnTime;
        d.health = health;
        d.speed = speed;
        return d;
    }

    static void Apply(string version, float levelTime, int[] nextExp, SpawnData[] spawnData)
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Balance", "플레이 중에는 적용할 수 없어요. ▶를 멈춘 뒤 다시 실행해 주세요.", "확인");
            return;
        }

        GameManager gm = Object.FindObjectOfType<GameManager>(true);
        Spawner spawner = Object.FindObjectOfType<Spawner>(true);
        if (gm == null || spawner == null)
        {
            EditorUtility.DisplayDialog("Balance", "GameManager 또는 Spawner를 찾지 못했어요. SampleScene을 연 상태에서 실행해 주세요.", "확인");
            return;
        }

        Undo.RecordObject(gm, "Apply Balance " + version);
        Undo.RecordObject(spawner, "Apply Balance " + version);

        gm.balanceVersion = version;
        gm.nextExp = nextExp;
        spawner.levelTime = levelTime;
        spawner.spawnData = spawnData;

        EditorUtility.SetDirty(gm);
        EditorUtility.SetDirty(spawner);
        EditorSceneManager.MarkSceneDirty(gm.gameObject.scene);

        Debug.Log("[Balance] " + version + " 적용 완료 (스폰 단계 " + spawnData.Length + "개, " + levelTime +
                  "초 간격, 경험치 단계 " + nextExp.Length + "개). Ctrl + S로 씬을 저장하세요.");
    }
}
