using UnityEngine;
using TMPro;
using CPS.ICPBL.Scoring;

public class ScoreboardScript : MonoBehaviour
{
    [Header("Data Sources")]
    [Tooltip("2026 ICPBLScorer. 연결 시 새 점수 계산 + idle/conflict HUD 표시.")]
    public ICPBLScorer scorer;

    public SpawnSystem spawnSystem;
    public BoxTrigger normalBox;
    public BoxTrigger abnormalBox;

    [Header("Core HUD")]
    public TextMeshProUGUI seedText;
    public TextMeshProUGUI timeText;
    public TextMeshProUGUI scoreText;

    [Header("Extended HUD (선택 — 미연결 시 표시 skip)")]
    public TextMeshProUGUI correctText;
    public TextMeshProUGUI wrongText;
    public TextMeshProUGUI unplacedText;
    public TextMeshProUGUI idleText;
    public TextMeshProUGUI conflictText;

    private int legacyScore = 0;

    void Start()
    {
        if (seedText != null && spawnSystem != null)
            seedText.text = $"Seed : {spawnSystem.seed}";
        if (scoreText != null)
            scoreText.text = "Score : 0 / 15";

        if (normalBox != null) normalBox.isNormalBox = true;
        if (abnormalBox != null) abnormalBox.isNormalBox = false;
    }

    void Update()
    {
        // 시드는 시뮬 시작 시점에 갱신 (UISystem 이 직접 갱신하지만 fallback)
        if (seedText != null && spawnSystem != null)
            seedText.text = $"Seed : {spawnSystem.seed}";

        // 시간 표시: scorer 연결 시 scorer.CurrentTime, 아니면 Time.timeSinceLevelLoad
        if (timeText != null)
        {
            float t = scorer != null ? scorer.CurrentTime : Time.timeSinceLevelLoad;
            timeText.text = $"Time : {t:F2}";
        }

        if (scorer != null)
        {
            // ICPBLScorer 기반 HUD ("라벨 : 값" 형식)
            if (scoreText != null) scoreText.text = $"Score : {scorer.CurrentScore} / 15";
            if (correctText != null) correctText.text = $"Correct : {scorer.CorrectCount}";
            if (wrongText != null) wrongText.text = $"Wrong : {scorer.WrongCount}";
            if (unplacedText != null) unplacedText.text = $"Unplaced : {scorer.UnplacedCount}";
            if (idleText != null)
                idleText.text = $"Idle : {scorer.BothIdleTime:F1}s (-{scorer.IdlePenalty})";
            if (conflictText != null)
                conflictText.text = $"Conflict : {scorer.TotalConflicts} (-{scorer.ConflictPenalty})";
        }
        else if (normalBox != null && abnormalBox != null && spawnSystem != null)
        {
            // Fallback: scorer 미연결 시 단순 cube 카운트 기반 score
            legacyScore = normalBox.correctCount + abnormalBox.correctCount
                        - normalBox.wrongCount - abnormalBox.wrongCount;
            if (scoreText != null)
                scoreText.text = $"Score : {legacyScore} / {spawnSystem.totalProductNumber}";
        }
    }
}
