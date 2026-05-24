# CPS 2026 IC-PBL 4인 역할 분담 및 슬라이스별 과업 정의서

> 기준 자료
>
> - 과제 설명 Notion: https://smooth-kidney-f73.notion.site/2026-CPS-IC-PBL-8cf70ecf442e82eeb3f801fbf200be5c
> - GitHub 레포: https://github.com/leeyj3382/CPS_2026
> - 기존 과업 정의서: `CPS_2026_TaskOverview.md`
>
> 전제
>
> - Unity 씬 구성, RobotA/RobotB, 컨베이어, 박스, 채점기, UI는 기본적으로 제공되어 있다고 가정한다.
> - 학생 코드는 `Assets/CPS_IC_PBL_2026/Scripts/Student/` 아래에 작성한다.
> - `[LOCKED] BaseAssets` 하위 원본 스크립트/프리팹은 직접 수정하지 않는다.
> - 역할 분담은 4명이 병렬 개발할 수 있도록 나누되, 각 슬라이스의 연결 스키마를 먼저 고정한다.

---

## 1. 전체 결론

4명이서 수행할 경우 역할은 아래 4개 슬라이스로 나누는 것이 가장 안전하다.

| 슬라이스 | 역할명                                  | 핵심 책임                                                         |
| -------- | --------------------------------------- | ----------------------------------------------------------------- |
| Slice A  | Fleet / Scheduling / Common Schema      | 전체 제어 흐름, 공통 데이터 스키마, 큐 감시, 작업 생성/할당       |
| Slice B  | Robot Mission / Motion / Gripper        | 로봇 상태 머신, 이동, Pick, Grip, Inspect, Place 미션 실행        |
| Slice C  | Pose / Calibration / Palletizing        | 컨베이어별 Pick 좌표, 박스 Place 좌표, 슬롯 기반 팔레타이징       |
| Slice D  | Vision / Safety / Telemetry / Bootstrap | 색상 분류, 자원 Lock, 충돌/Deadlock 방지, Telemetry, 씬 참조 연결 |

핵심 의존성은 다음과 같다.

```text
Slice A가 WorkTask를 만든다.
  ↓
Slice A가 Slice B의 RobotAgent.StartMission()에 MissionRequest를 넘긴다.
  ↓
Slice B는 미션 수행 중 Slice D의 LockManager를 호출한다.
  ↓
Slice B는 Slice C의 PoseProvider/Palletizer에서 Pick/Place 좌표를 받는다.
  ↓
Slice B는 Slice D의 ColorClassifier로 Normal/Abnormal을 판정한다.
  ↓
Slice B가 MissionResult를 Slice A에 반환한다.
  ↓
Slice A는 task/reservation 상태를 갱신하고 다음 작업을 할당한다.
```

따라서 **공통 스키마와 인터페이스를 먼저 맞추는 것이 1순위**다. 스키마가 흔들리면 4명이 만든 코드가 합쳐지지 않는다.

---

## 2. 공통 개발 규칙

### 2.1 코드 작성 위치

모든 학생 코드는 아래 경로에 둔다.

```text
Assets/CPS_IC_PBL_2026/Scripts/Student/
```

권장 폴더 구조는 다음과 같다.

```text
Assets/CPS_IC_PBL_2026/Scripts/Student/
├── Common/
│   ├── StudentEnums.cs
│   ├── StudentSchemas.cs
│   ├── StudentInterfaces.cs
│   └── StudentConstants.cs
│
├── Bootstrap/
│   ├── StudentBootstrap.cs
│   └── StudentSceneReferences.cs
│
├── Fleet/
│   ├── FleetManager.cs
│   ├── EnvironmentScanner.cs
│   └── TaskAllocator.cs
│
├── Robot/
│   ├── RobotAgent.cs
│   ├── MissionExecutor.cs
│   └── GripperAdapter.cs
│
├── Pose/
│   ├── PoseTable.cs
│   ├── CalibrationManager.cs
│   └── Palletizer.cs
│
├── Vision/
│   └── ColorClassifier.cs
│
├── Safety/
│   ├── ResourceLockManager.cs
│   ├── PathPlanner.cs
│   └── DeadlockGuard.cs
│
└── Telemetry/
    └── TelemetryLogger.cs
```

`Common` 폴더의 스키마와 인터페이스는 모든 팀원이 공유한다. 먼저 PR로 올리고, 이후 각자 슬라이스를 붙인다.

`Bootstrap`은 씬 참조 연결과 전체 초기화만 담당한다. Unity scene 파일 충돌을 줄이기 위해 `StudentSceneReferences`의 Inspector 연결은 Slice D 또는 팀장 1명이 단독으로 관리한다.

### 2.2 수정 금지

아래는 직접 수정하지 않는다.

```text
Assets/CPS_IC_PBL_2026/[LOCKED] BaseAssets/
```

직접 수정하지 말아야 할 항목:

- RobotController 원본
- UR5e IK 원본
- SuctionGripper 원본
- ColorSensor / ColorArea 원본
- Conveyor / Queue / Box 환경 원본
- 채점기 / Scoreboard / UI 원본
- Prefab 원본

허용 방식:

- `Student` 폴더에 새 스크립트 작성
- 씬의 인스턴스에 새 스크립트 부착
- 기존 컴포넌트를 `SerializeField`로 참조
- `IRobotController`, `IEnvironmentInfo`, `SuctionGripper`, `ColorSensor`/`ColorArea`, `BoxTrigger` 등의 공개 API 호출

### 2.3 Scene 변경 담당자

Unity scene 파일은 충돌이 자주 난다. 따라서 **씬 reference 연결은 Slice D 또는 팀장이 단독 관리**한다.

원칙:

- 모든 팀원이 동시에 씬 파일을 수정하지 않는다.
- 로직 코드는 각자 `.cs` 파일에서 개발한다.
- Inspector reference 연결은 한 명이 최종 통합 시 담당한다.
- 다른 팀원은 코드에서 `FindObjectOfType()` 남발하지 말고 `StudentSceneReferences`를 통해 참조를 받는다.

---

## 3. 공통 스키마

아래 스키마는 모든 슬라이스가 동일하게 사용해야 한다. 실제 구현 시 파일명은 `StudentSchemas.cs`, `StudentInterfaces.cs` 등으로 분리해도 된다.

중요: `Student/Common`은 `[LOCKED] BaseAssets/Common`을 대체하는 계층이 아니다. `[LOCKED]`의 `CPS.ICPBL.Common.ClassificationResult`, `CPS.ICPBL.Common.BoxType` 같은 공식 타입을 재사용하면서, 학생 코드 내부 모듈 간 계약만 추가로 정의한다. 기존 학생용 `ProductClass`는 더 이상 사용하지 않는다.

### 3.1 공통 enum

```csharp
namespace CPS.ICPBL.Student
{
    public enum RobotRuntimeState
    {
        Idle = 0,
        Reserved = 1,
        MovingToConveyor = 2,
        Picking = 3,
        Retracting = 4,
        Inspecting = 5,
        MovingToBox = 6,
        Placing = 7,
        Releasing = 8,
        Completed = 9,
        Failed = 10,
        WaitingForLock = 11,
        Stuck = 12
    }

    public enum TaskStatus
    {
        Pending = 0,
        Reserved = 1,
        Running = 2,
        Completed = 3,
        Failed = 4,
        Cancelled = 5
    }

    // [LOCKED] ResourceType 대체가 아니라 CentralZone / RobotArmZone까지 포함한 학생용 확장 lock 타입.
    public enum LockResourceType
    {
        Conveyor = 0,
        NormalBox = 1,
        AbnormalBox = 2,
        CentralZone = 3,
        RobotArmZone = 4
    }

    public enum MissionFailureReason
    {
        None = 0,
        QueueEmpty = 1,
        MoveTimeout = 2,
        GripFailed = 3,
        ClassificationFailed = 4,
        BoxLockFailed = 5,
        PlaceFailed = 6,
        CollisionRisk = 7,
        Unknown = 99
    }
}
```

### 3.2 공통 데이터 모델

```csharp
using CPS.ICPBL.Common;
using UnityEngine;

namespace CPS.ICPBL.Student
{
    [System.Serializable]
    public class ConveyorSnapshot
    {
        public int conveyorId;          // 1~10
        public int queueLength;         // 0~3
        public float productionPeriod;  // 15, 18, 20, ...
        public float nextProductionAt;  // IEnvironmentInfo.NextProductionAt(id)
        public float lastAssignedAt;
        public bool isReserved;
    }

    [System.Serializable]
    public class StudentRobotSnapshot
    {
        public RobotSnapshot baseSnapshot;   // [LOCKED] Common.RobotSnapshot
        public RobotRuntimeState state;
        public int currentStationId;
        public int currentTaskId;
    }

    [System.Serializable]
    public class WorkTask
    {
        public int taskId;
        public int conveyorId;              // 1~10
        public int assignedRobotId;         // -1이면 미배정
        public float createdAt;
        public float assignedAt;
        public float priorityScore;
        public TaskStatus status;
        public int retryCount;
        public string debugReason;
    }

    [System.Serializable]
    public class MissionRequest
    {
        public int taskId;
        public int robotId;
        public int conveyorId;
        public float requestTime;
        public float timeoutSec;
    }

    [System.Serializable]
    public class MissionResult
    {
        public int taskId;
        public int robotId;
        public int conveyorId;
        public bool success;
        public ClassificationResult classificationResult;
        public int destinationStationId;     // Normal=100, Abnormal=101
        public MissionFailureReason failureReason;
        public string message;
        public float startedAt;
        public float finishedAt;
    }

    [System.Serializable]
    public class StationPose
    {
        public int stationId;                // 1~10, 100, 101
        public Vector3 approachPos;
        public Vector3 actionPos;            // conveyor면 pick 위치, box면 place 위치
        public Vector3 retractPos;
        public float armMoveDuration = 1.0f;
    }

    [System.Serializable]
    public class BoxSlotPose
    {
        public BoxType boxType;
        public int stationId;                // Normal=100, Abnormal=101
        public int slotIndex;
        public Vector3 approachPos;
        public Vector3 placePos;
        public Vector3 retractPos;
        public bool reserved;
        public int reservedByTaskId;
    }

    [System.Serializable]
    public class ColorClassificationResult
    {
        public ClassificationResult result;
        public Color sensedColor;
        public float blueDistance;
        public float redDistance;
        public bool reliable;
        public string message;
    }

    [System.Serializable]
    public struct ResourceKey : System.IEquatable<ResourceKey>
    {
        public LockResourceType type;
        public int id; // Conveyor는 1~10, NormalBox=100, AbnormalBox=101, CentralZone=0 등

        public ResourceKey(LockResourceType type, int id)
        {
            this.type = type;
            this.id = id;
        }

        public bool Equals(ResourceKey other)
        {
            return type == other.type && id == other.id;
        }

        public override bool Equals(object obj)
        {
            return obj is ResourceKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ((int)type * 397) ^ id;
        }

        public override string ToString()
        {
            return $"{type}:{id}";
        }
    }

    [System.Serializable]
    public class ResourceLockToken
    {
        public ResourceKey key;
        public int robotId;
        public int taskId;
        public float acquiredAt;
    }
}
```

주의: `ResourceKey`는 lock dictionary의 key로 사용되므로 `class`가 아니라 `struct + Equals/GetHashCode` 형태로 둔다. 그래야 `(Conveyor, 1)` 같은 동일 자원이 객체 인스턴스 차이 때문에 다른 key로 취급되는 문제를 막을 수 있다.

### 3.3 공통 인터페이스

각 슬라이스는 아래 인터페이스만 보고 연결될 수 있어야 한다.

```csharp
using System;
using CPS.ICPBL.Common;
using UnityEngine;

namespace CPS.ICPBL.Student
{
    public interface IRobotAgent
    {
        int RobotId { get; }
        RobotRuntimeState State { get; }
        bool CanAcceptTask { get; }
        void StartMission(MissionRequest request, Action<MissionResult> onFinished);
    }

    public interface ITaskAllocator
    {
        WorkTask SelectBestTask(
            ConveyorSnapshot[] conveyors,
            StudentRobotSnapshot robot,
            WorkTask[] pendingTasks
        );
    }

    public interface IPoseProvider
    {
        StationPose GetConveyorPickPose(int conveyorId);
        StationPose GetBoxBasePose(BoxType boxType);
    }

    public interface IPalletizer
    {
        BoxSlotPose ReserveNextSlot(BoxType boxType, int robotId, int taskId);
        void CommitSlot(int taskId);
        void ReleaseSlot(int taskId);
    }

    public interface IColorClassifier
    {
        ColorClassificationResult Classify(Color sensedColor);
    }

    public interface IResourceLockManager
    {
        bool TryAcquire(ResourceKey key, int robotId, int taskId, out ResourceLockToken token);
        void Release(ResourceLockToken token);
        bool IsLocked(ResourceKey key);
    }

    public interface IPathPlanner
    {
        bool RequiresCentralZone(int robotId, int fromStationId, int toStationId);
    }

    public interface ITelemetryLogger
    {
        void LogTaskCreated(WorkTask task);
        void LogTaskAssigned(WorkTask task, int robotId);
        void LogMissionResult(MissionResult result);
        void LogLock(string action, ResourceKey key, int robotId, int taskId);
        void LogMessage(string category, string message);
    }
}
```

### 3.4 Scene reference 스키마

씬의 RobotA, RobotB, EnvironmentInfo, Gripper, ColorSensor/ColorArea, BoxTrigger 등을 한 곳에서 관리한다. 파일 위치는 `Student/Bootstrap/StudentSceneReferences.cs`로 둔다.

```csharp
using UnityEngine;
using CPS.ICPBL.Common;
using CPS.ICPBL.Robots;
using CPS.Lab11.MobileManipulator;

namespace CPS.ICPBL.Student
{
    public class StudentSceneReferences : MonoBehaviour
    {
        [Header("Robot Controllers")]
        public RobotController robotAController;
        public RobotController robotBController;

        [Header("Environment")]
        public MonoBehaviour environmentInfoSource; // IEnvironmentInfo로 cast해서 사용

        [Header("Grippers")]
        public SuctionGripper robotAGripper;
        public SuctionGripper robotBGripper;

        [Header("Color Sensors / Areas")]
        public ColorSensor robotAColorSensor;
        public ColorSensor robotBColorSensor;
        public ColorArea robotAColorArea;
        public ColorArea robotBColorArea;

        [Header("Boxes")]
        public BoxTrigger normalBoxTrigger;
        public BoxTrigger abnormalBoxTrigger;

        public IEnvironmentInfo EnvironmentInfo => environmentInfoSource as IEnvironmentInfo;
        public ColorArea RobotAColorArea => robotAColorArea != null ? robotAColorArea : robotAColorSensor?.area;
        public ColorArea RobotBColorArea => robotBColorArea != null ? robotBColorArea : robotBColorSensor?.area;
    }
}
```

주의:

- `environmentInfoSource`는 Inspector에서 실제 `IEnvironmentInfo` 구현 컴포넌트를 연결한다.
- `RobotController`는 레포 기준 `IRobotController`를 구현한다.
- `ColorSensor` 또는 `ColorArea`를 연결할 수 있지만, 실제 색상값은 `ColorArea.color`에서 읽는다.
- `BoxTrigger`는 Palletizer가 slot occupancy를 직접 검증하거나 등록할 때 사용한다.
- Scene reference 연결은 한 명이 담당해 merge conflict를 줄인다.

---

## 4. 슬라이스별 상세 역할

---

# Slice A. Fleet / Scheduling / Common Schema

## A-1. 한 줄 역할

전체 시스템의 두뇌 역할이다. 10개 컨베이어의 큐 상태를 읽고, 어떤 작업을 먼저 처리할지 계산한 뒤, RobotA/B 중 가능한 로봇에게 `MissionRequest`를 전달한다.

## A-2. 담당 파일

```text
Student/Common/StudentEnums.cs
Student/Common/StudentSchemas.cs
Student/Common/StudentInterfaces.cs
Student/Common/StudentConstants.cs
Student/Fleet/FleetManager.cs
Student/Fleet/TaskAllocator.cs
Student/Fleet/EnvironmentScanner.cs
```

## A-3. 주요 책임

### 1) 공통 스키마 확정

Slice A는 프로젝트 초기에 공통 enum, 데이터 모델, interface를 먼저 만든다.

필수 산출물:

- `[LOCKED] CPS.ICPBL.Common.ClassificationResult` 재사용
- `[LOCKED] CPS.ICPBL.Common.BoxType` 재사용
- `RobotRuntimeState`
- `TaskStatus`
- `MissionFailureReason`
- `ConveyorSnapshot`
- `StudentRobotSnapshot`
- `WorkTask`
- `MissionRequest`
- `MissionResult`
- `StationPose`
- `BoxSlotPose`
- `ColorClassificationResult`
- `ResourceKey`
- `ResourceLockToken`
- `IRobotAgent`
- `ITaskAllocator`
- `IPoseProvider`
- `IPalletizer`
- `IColorClassifier`
- `IResourceLockManager`
- `ITelemetryLogger`

다른 팀원은 이 스키마를 기준으로 개발한다. 스키마 변경이 필요하면 팀 전체에 공유하고 PR로 반영한다.

### 2) EnvironmentScanner 구현

`IEnvironmentInfo`를 이용해 컨베이어 상태를 스냅샷으로 변환한다.

입력:

```csharp
IEnvironmentInfo env;
```

출력:

```csharp
ConveyorSnapshot[] conveyors;
```

처리 내용:

- `GetQueueLength(1~10)` 호출
- `NextProductionAt(1~10)` 호출
- 컨베이어별 생산 주기 매핑
- 예약 여부 반영
- 큐가 0인 컨베이어는 작업 후보에서 제외

주의: 현재 레포 구현에서 `NextProductionAt()`은 `-1`을 반환할 수 있다. 이 경우 `GetQueueLength()` polling과 컨베이어 생산 주기 상수를 우선 사용한다.

컨베이어 생산 주기 상수:

```csharp
static readonly Dictionary<int, float> ConveyorPeriods = new()
{
    {1, 15f}, {2, 18f}, {3, 20f}, {4, 20f}, {5, 30f},
    {6, 36f}, {7, 45f}, {8, 45f}, {9, 60f}, {10, 90f}
};
```

### 3) TaskAllocator 구현

`ConveyorSnapshot[]`와 `StudentRobotSnapshot`을 받아 가장 처리할 가치가 높은 작업을 고른다.

권장 점수식:

```text
priorityScore =
    queueLength * 100
  + overflowRiskScore
  + speedScore
  + waitingScore
  - distanceCost
  - reservationPenalty
```

권장 기준:

- `queueLength == 3`: 최우선
- `queueLength == 2`이고 생산 주기가 짧은 컨베이어: 우선
- 1~4번 컨베이어: 기본 우선순위 높음
- 이미 예약된 컨베이어: 제외
- idle 로봇과 너무 먼 컨베이어: 점수 감점

### 4) FleetManager 구현

FleetManager는 전체 루프를 관리한다.

동작 흐름:

```text
1. EnvironmentScanner로 queue snapshot 갱신
2. RobotA/B 상태 snapshot 갱신
3. idle robot 찾기
4. TaskAllocator로 작업 선택
5. WorkTask 생성 또는 기존 pending task 선택
6. conveyor reservation 설정
7. MissionRequest 생성
8. RobotAgent.StartMission(request, callback) 호출
9. callback으로 MissionResult 수신
10. 성공/실패에 따라 task/reservation 갱신
11. 다음 loop 반복
```

FleetManager가 직접 하지 말아야 할 일:

- 팔 좌표 계산
- 직접 `MoveArmTo()` 호출
- 직접 `TryGrip()` 호출
- 직접 색상 판정
- 직접 박스 슬롯 좌표 계산
- 직접 물리 충돌 처리

이것들은 각각 Slice B/C/D의 책임이다.

## A-4. 다른 슬라이스와의 연결 스키마

### A → B: 작업 요청

A는 B의 `IRobotAgent.StartMission()`만 호출한다.

```csharp
MissionRequest request = new MissionRequest
{
    taskId = task.taskId,
    robotId = robot.RobotId,
    conveyorId = task.conveyorId,
    requestTime = env.CurrentTime,
    timeoutSec = 30f
};

robotAgent.StartMission(request, OnMissionFinished);
```

### B → A: 작업 결과

B는 미션 종료 시 `MissionResult`를 반환한다.

```csharp
void OnMissionFinished(MissionResult result)
{
    if (result.success)
    {
        // task completed
        // conveyor reservation release
        // completed count update
    }
    else
    {
        // retry or fail
        // reservation release
    }
}
```

### A ↔ D: 로그 및 상태

A는 작업 생성/할당/완료를 `ITelemetryLogger`에 남긴다.

```csharp
logger.LogTaskCreated(task);
logger.LogTaskAssigned(task, robotId);
logger.LogMissionResult(result);
```

A는 물리적 station lock은 직접 잡지 않고, **컨베이어 예약 상태만 관리**한다. 실제 station lock은 B가 미션 수행 중 D의 `ResourceLockManager`를 호출해 획득한다.

## A-5. 종속성

| 대상 슬라이스 | 종속 내용                         | 작업 순서                                                    |
| ------------- | --------------------------------- | ------------------------------------------------------------ |
| Slice B       | `IRobotAgent.StartMission()` 필요 | A는 먼저 DummyRobotAgent로 테스트 가능. 이후 B 구현체로 교체 |
| Slice C       | 직접 종속 낮음                    | C의 Pose/Palletizer는 B가 사용하므로 A는 몰라도 됨           |
| Slice D       | TelemetryLogger 사용              | D가 늦으면 A는 `Debug.Log` 기반 임시 logger 사용 가능        |

## A-6. 작업 순서

1. `Common` 스키마 작성
2. `EnvironmentScanner` 작성
3. 1~10번 queue length 콘솔 출력
4. `TaskAllocator` 작성
5. `DummyRobotAgent`로 할당 테스트
6. B의 실제 `RobotAgent`와 연결
7. MissionResult callback 처리
8. reservation/retry 정책 추가
9. 성능 최적화

## A-7. 완료 기준

- [ ] 공통 스키마가 컴파일된다.
- [ ] `GetQueueLength(1~10)`를 읽어 `ConveyorSnapshot[]`을 만든다.
- [ ] 큐가 0인 컨베이어에는 작업을 만들지 않는다.
- [ ] 큐가 3인 컨베이어가 최우선 배정된다.
- [ ] RobotA/B 중 idle 로봇에게만 작업을 준다.
- [ ] 같은 컨베이어가 두 로봇에게 중복 배정되지 않는다.
- [ ] MissionResult 수신 후 reservation이 해제된다.
- [ ] 실패 시 retryCount가 증가하거나 task가 실패 처리된다.

---

# Slice B. Robot Mission / Motion / Gripper

## B-1. 한 줄 역할

로봇 1대가 실제로 물품 하나를 처리하는 과정을 책임진다. A가 넘긴 `MissionRequest`를 받아서 이동, Pick, Grip, 색상검사 호출, 박스 이동, Place, Release까지 수행하고 `MissionResult`를 반환한다.

중요: Slice B는 RobotA만 전담하는 역할이 아니다. **단일 로봇용 `RobotAgent` / `MissionExecutor`를 만들고, 같은 구현을 RobotA와 RobotB 두 인스턴스에 모두 붙이거나 생성할 수 있게 만드는 역할**이다. 두 로봇 중 누구에게 어떤 작업을 줄지는 Slice A가 결정하고, RobotA/B의 controller, gripper, colorArea 참조 연결은 Slice D의 Bootstrap이 담당한다.

## B-2. 담당 파일

```text
Student/Robot/RobotAgent.cs
Student/Robot/MissionExecutor.cs
Student/Robot/GripperAdapter.cs
```

## B-3. 주요 책임

### 1) RobotAgent 상태 머신

RobotAgent는 RobotA/B 각각에 붙거나, FleetManager가 두 인스턴스를 생성해 관리한다. 따라서 Slice B 구현은 `robotId`, controller, gripper, colorArea 같은 instance별 참조만 바뀌면 RobotA/B 양쪽에서 동일하게 동작해야 한다.

필수 상태:

```text
Idle
Reserved
MovingToConveyor
Picking
Retracting
Inspecting
MovingToBox
Placing
Releasing
Completed
Failed
WaitingForLock
Stuck
```

### 2) MissionExecutor 구현

`MissionRequest` 하나를 받아 전체 미션을 수행한다.

정상 미션 흐름:

```text
1. PathPlanner로 CentralZone lock 필요 여부 확인
2. 필요 시 CentralZone lock 요청
3. Conveyor lock 요청
4. GoToOperatingStation(conveyorId)
5. IsBusy == false가 될 때까지 대기
6. Slice C의 PoseProvider에서 conveyor pick pose 요청
7. approachPos로 MoveArmTo()
8. IsBusy == false가 될 때까지 대기
9. actionPos로 MoveArmTo()
10. IsBusy == false가 될 때까지 대기
11. GripperAdapter.WaitUntilGraspReady()
12. TryGrip()
13. IsHolding 확인
14. retractPos로 MoveArmTo()
15. IsBusy == false가 될 때까지 대기
16. Slice D의 ColorClassifier로 색상 판정
17. 목적 station 결정: Normal=100, Abnormal=101
18. Conveyor lock 해제
19. 필요 시 CentralZone lock 해제 또는 다음 이동까지 유지
20. Box lock 요청
21. GoToOperatingStation(100 또는 101)
22. IsBusy == false가 될 때까지 대기
23. Slice C의 Palletizer에서 다음 slot 예약
24. slot.approachPos로 MoveArmTo()
25. IsBusy == false가 될 때까지 대기
26. slot.placePos로 MoveArmTo()
27. IsBusy == false가 될 때까지 대기
28. Release()
29. slot.retractPos로 MoveArmTo()
30. IsBusy == false가 될 때까지 대기
31. Palletizer.CommitSlot(taskId)
32. Box lock 해제
33. MissionResult success 반환
```

### 3) GripperAdapter 구현

SuctionGripper 직접 호출을 감싸는 adapter를 만든다.

권장 메서드:

```csharp
public class GripperAdapter
{
    public bool IsHolding { get; }
    public bool IsGraspReady { get; }

    public IEnumerator WaitUntilGraspReady(float timeoutSec);
    public bool CanGrip(out string reason);
    public bool TryGripWithRetry(int retryCount, float waitBetweenRetrySec);
    public void Release();
}
```

처리해야 할 조건:

- `CurrentCandidate != null`인지 확인
- `IsGraspReady == true`인지 확인
- 실패 시 `CanGrip(out reason)`으로 원인을 기록
- 접촉 안정화를 위해 최소 0.2초 이상 대기
- `TryGrip()` 후 `IsHolding == true`인지 확인
- 실패 시 pose 미세 조정은 Slice C와 협의

## B-4. 다른 슬라이스와의 연결 스키마

### A → B: MissionRequest 수신

```csharp
public void StartMission(MissionRequest request, Action<MissionResult> onFinished)
```

B는 `request.conveyorId`만 보고 어느 컨베이어로 갈지 판단한다. 작업 우선순위나 배정 판단은 하지 않는다.

### B → C: Pose 요청

```csharp
StationPose pose = poseProvider.GetConveyorPickPose(request.conveyorId);
```

- `approachPos`: 물품 위쪽 접근 위치
- `actionPos`: 실제 pick 위치
- `retractPos`: grip 후 안전하게 들어 올릴 위치

### B → D: Conveyor lock 요청

```csharp
var conveyorKey = new ResourceKey(LockResourceType.Conveyor, request.conveyorId);
if (!lockManager.TryAcquire(conveyorKey, request.robotId, request.taskId, out var conveyorToken))
{
    // 대기 또는 실패 처리
}
```

### B → D: 색상 분류 요청

```csharp
Color sensed = colorArea.color;
ColorClassificationResult classification = colorClassifier.Classify(sensed);
```

B는 색상 판정 로직을 직접 작성하지 않는다. D의 ColorClassifier만 호출한다. `classification.result`는 `[LOCKED]` 공식 `ClassificationResult`이며, 목적 박스가 필요할 때만 `StudentConstants.TryGetBoxType()`으로 `BoxType`으로 변환한다.

### B → C: Box slot 요청

```csharp
if (!StudentConstants.TryGetBoxType(classification.result, out BoxType boxType))
{
    // 재검사 또는 ClassificationFailed 처리
}

BoxSlotPose slot = palletizer.ReserveNextSlot(
    boxType,
    request.robotId,
    request.taskId
);
```

Place 성공 시:

```csharp
palletizer.CommitSlot(request.taskId);
```

실패 시:

```csharp
palletizer.ReleaseSlot(request.taskId);
```

### B → A: MissionResult 반환

```csharp
MissionResult result = new MissionResult
{
    taskId = request.taskId,
    robotId = request.robotId,
    conveyorId = request.conveyorId,
    success = true,
    classificationResult = classification.result,
    destinationStationId = destinationStationId,
    failureReason = MissionFailureReason.None,
    message = "OK"
};

onFinished?.Invoke(result);
```

## B-5. 종속성

| 대상 슬라이스 | 종속 내용                                         | 작업 순서                                                       |
| ------------- | ------------------------------------------------- | --------------------------------------------------------------- |
| Slice A       | MissionRequest 수신, MissionResult 반환           | A의 Common schema가 먼저 필요                                   |
| Slice C       | Pick/Place pose, Palletizer slot                  | C가 늦으면 B는 임시 hard-coded pose로 1개 컨베이어 테스트       |
| Slice D       | LockManager, PathPlanner, ColorClassifier, Logger | D가 늦으면 임시 lock manager와 임시 Normal 판정으로 테스트 가능 |

## B-6. 작업 순서

1. `RobotAgent` 기본 상태 머신 작성
2. `StartMission()`에서 단순히 station 이동만 수행
3. RobotA 기준 `GoToOperatingStation(1)` 성공 확인
4. Slice C의 임시 pick pose로 `MoveArmTo()` 테스트
5. GripperAdapter로 `IsGraspReady`, `TryGrip`, `IsHolding` 확인
6. Normal Box로 이동 후 임시 place pose에서 Release
7. Slice D의 ColorClassifier 연결
8. Slice C의 Palletizer slot 연결
9. Slice D의 LockManager 연결
10. RobotA/B 양쪽에서 동일 미션 수행
11. 실패 처리와 timeout 추가

## B-7. 완료 기준

- [ ] RobotA가 `MissionRequest(conveyorId=1)`를 받아 물품 1개를 처리한다.
- [ ] RobotB도 같은 방식으로 물품 1개를 처리한다.
- [ ] 같은 `RobotAgent` / `MissionExecutor` 구현을 RobotA/B 두 인스턴스에 재사용할 수 있다.
- [ ] `TryGrip()` 후 `IsHolding`을 확인한다.
- [ ] grip 실패 시 최소 1회 재시도한다.
- [ ] 색상 분류 결과에 따라 station 100 또는 101로 이동한다.
- [ ] Palletizer가 준 slot에 낮은 높이로 놓는다.
- [ ] 성공/실패 여부를 `MissionResult`로 A에게 반환한다.
- [ ] lock 획득 실패, grip 실패, 색상 실패, place 실패가 모두 failureReason에 기록된다.

---

# Slice C. Pose / Calibration / Palletizing

## C-1. 한 줄 역할

로봇이 물품을 제대로 집고 박스 안에 안정적으로 놓을 수 있도록 좌표를 책임진다. 이 슬라이스가 부정확하면 B의 미션 로직이 아무리 좋아도 grip/place가 실패한다.

## C-2. 담당 파일

```text
Student/Pose/PoseTable.cs
Student/Pose/CalibrationManager.cs
Student/Pose/Palletizer.cs
```

## C-3. 주요 책임

### 1) PoseTable 구현

컨베이어 1~10의 Pick pose를 제공한다.

필수 API:

```csharp
using CPS.ICPBL.Common;

public class PoseTable : MonoBehaviour, IPoseProvider
{
    public StationPose GetConveyorPickPose(int conveyorId);
    public StationPose GetBoxBasePose(BoxType boxType);
}
```

컨베이어 pose는 아래 구조를 따른다.

```text
approachPos: 물품 위쪽 접근 위치
 actionPos: 실제 pick 위치
retractPos: grip 후 들어올릴 안전 위치
```

초기값은 `OperatingStations.asset`의 `ArmAnchorPoint`를 기준으로 잡고, Unity에서 직접 테스트하며 보정한다.

### 2) CalibrationManager 구현

반복 테스트 중 pose를 빠르게 조정할 수 있도록 만든다.

권장 기능:

- 현재 선택한 conveyorId 출력
- `approachPos`, `actionPos`, `retractPos`를 Inspector에서 수정 가능하게 구성
- 키 입력 또는 버튼으로 현재 pose 테스트
- grip 성공/실패 결과 기록
- 잘 되는 pose를 `PoseTable`에 반영

### 3) Palletizer 구현

Normal/Abnormal 박스 안에 다음 slot 위치를 제공한다.

필수 API:

```csharp
using CPS.ICPBL.Common;

public class Palletizer : MonoBehaviour, IPalletizer
{
    public BoxSlotPose ReserveNextSlot(BoxType boxType, int robotId, int taskId);
    public void CommitSlot(int taskId);
    public void ReleaseSlot(int taskId);
}
```

slot 예약이 필요한 이유:

- 두 로봇이 동시에 같은 slot에 놓는 것을 방지
- Place 실패 시 slot index를 되돌릴 수 있음
- 박스 occupancy와 내부 slot 상태를 비교할 수 있음

slot 상태 관리 예시:

```text
Normal slots:
  index 0 reserved/committed
  index 1 reserved/committed
  ...

Abnormal slots:
  index 0 reserved/committed
  index 1 reserved/committed
  ...
```

박스 적재 검증:

- 가능하면 `IEnvironmentInfo.GetBoxOccupancy(BoxType box)` 값과 자체 slot index를 비교한다.
- `BoxTrigger` 직접 참조가 있으면 `IsSlotOccupied(slotIndex)`로 중복 여부를 확인하고, place 성공 후 `RegisterSlotPlacement(slotIndex)`로 점유를 등록할 수 있다.
- 두 값이 크게 어긋나면 slot commit 누락, release 실패, 박스 밖 이탈 가능성을 로그로 남긴다.
- `GetBoxOccupancy()`는 현재 레포에서 `BoxTrigger.OccupiedSlotCount`를 반환하므로, slot 등록을 하지 않으면 자체 slot index와 값이 다를 수 있다.
- occupancy 검증은 slot 좌표 계산 자체를 대체하지 않고, 팔레타이징 결과를 검증하는 보조 수단으로 사용한다.

## C-4. 다른 슬라이스와의 연결 스키마

### B → C: Pick pose 요청

```csharp
StationPose pose = poseProvider.GetConveyorPickPose(conveyorId);
```

반환 조건:

- `stationId == conveyorId`
- `approachPos`, `actionPos`, `retractPos`가 모두 유효한 world position
- B는 이 좌표를 그대로 `MoveArmTo()`에 넣을 수 있어야 함

### B → C: Place slot 예약

```csharp
BoxSlotPose slot = palletizer.ReserveNextSlot(boxType, robotId, taskId);
```

반환 조건:

- `boxType == BoxType.Normal`이면 `stationId = 100`
- `boxType == BoxType.Abnormal`이면 `stationId = 101`
- `approachPos`, `placePos`, `retractPos` 제공
- `slotIndex` 제공
- 해당 taskId로 slot reserved 처리

### B → C: Place 성공/실패 통보

성공:

```csharp
palletizer.CommitSlot(taskId);
```

실패:

```csharp
palletizer.ReleaseSlot(taskId);
```

## C-5. 종속성

| 대상 슬라이스 | 종속 내용                             | 작업 순서                                             |
| ------------- | ------------------------------------- | ----------------------------------------------------- |
| Slice B       | B가 pose를 사용해 실제 MoveArmTo 실행 | C는 B의 단일 로봇 테스트와 함께 보정해야 함           |
| Slice D       | Box lock과 slot 예약 순서             | B가 box lock 획득 후 C의 slot을 reserve하는 순서 권장 |
| Slice A       | 직접 종속 낮음                        | A는 pose를 몰라도 됨                                  |

## C-6. 작업 순서

1. `PoseTable` 기본 구조 작성
2. 컨베이어 1번 pick pose부터 측정
3. RobotA 기준 grip 성공 확인
4. 컨베이어 2~10 pose 확장
5. Normal Box place pose 측정
6. Abnormal Box place pose 측정
7. `Palletizer.ReserveNextSlot(BoxType boxType, int robotId, int taskId)` 구현
8. 1층 grid slot 배치 구현
9. Place 후 박스 밖 이탈/겹침 확인
10. 필요하면 2층 적재 검토

## C-7. 완료 기준

- [ ] 컨베이어 1~10에 대해 `GetConveyorPickPose()`가 동작한다.
- [ ] 각 pose로 접근했을 때 grip 후보가 잡힌다.
- [ ] Normal Box slot이 순서대로 증가한다.
- [ ] Abnormal Box slot이 순서대로 증가한다.
- [ ] `ReserveNextSlot()`과 `CommitSlot()`이 구분된다.
- [ ] Place 실패 시 `ReleaseSlot()`으로 예약을 되돌릴 수 있다.
- [ ] `GetBoxOccupancy()`와 자체 slot index가 크게 어긋나지 않는지 확인한다.
- [ ] 물품이 박스 밖으로 나가지 않는다.
- [ ] 자유낙하가 발생하지 않도록 낮은 placePos를 제공한다.

---

# Slice D. Vision / Safety / Telemetry / Bootstrap

## D-1. 한 줄 역할

색상 검사, 자원 Lock, 충돌/Deadlock 방지, 로그, 씬 참조 연결 및 전체 시스템 초기화를 담당한다.  
즉, Slice D는 **로봇이 안전하게 움직이고, 색상 판정 결과가 안정적으로 나오며, 다른 슬라이스가 공통 참조와 로그를 통해 연결될 수 있게 만드는 역할**이다.

중요: **QA / 빌드 / 제출 검증은 Slice D의 단독 책임이 아니다.**  
해당 항목은 개발 완료 후 진행하는 **공통 후반 작업**으로 별도 관리한다.

## D-2. 담당 파일

```text
Student/Bootstrap/StudentBootstrap.cs
Student/Bootstrap/StudentSceneReferences.cs
Student/Vision/ColorClassifier.cs
Student/Safety/ResourceLockManager.cs
Student/Safety/PathPlanner.cs
Student/Safety/DeadlockGuard.cs
Student/Telemetry/TelemetryLogger.cs
```

Scene reference의 Inspector 연결은 Unity scene 충돌을 줄이기 위해 Slice D 또는 팀장 1명이 단독으로 관리한다.  
단, 공통 스키마 자체는 Slice A가 먼저 확정한다.

## D-3. 주요 책임

### 1) StudentBootstrap / StudentSceneReferences 관리

Slice D는 씬에 존재하는 주요 컴포넌트 참조를 한 곳에 모아 다른 슬라이스가 사용할 수 있게 한다.

관리 대상:

- RobotA Controller
- RobotB Controller
- EnvironmentInfo
- RobotA Gripper
- RobotB Gripper
- RobotA ColorSensor 또는 ColorArea
- RobotB ColorSensor 또는 ColorArea
- Normal BoxTrigger
- Abnormal BoxTrigger
- FleetManager
- PoseTable / Palletizer
- ColorClassifier
- ResourceLockManager
- PathPlanner
- TelemetryLogger

해야 할 일:

- `StudentSceneReferences` 컴포넌트를 씬에 배치한다.
- Inspector에서 필요한 reference를 연결한다.
- `StudentBootstrap`에서 FleetManager, RobotAgent, PoseProvider, LockManager, Logger 등이 서로 연결되도록 초기화한다.
- Scene 파일 충돌을 막기 위해 Inspector reference 연결은 한 명이 관리한다.
- 다른 팀원이 `FindObjectOfType()`를 남발하지 않도록 공통 참조 경로를 제공한다.

### 2) ColorClassifier 구현

`ColorArea.color`에서 읽은 색상을 받아 Normal/Abnormal을 판정한다. Robot 쪽에서는 `ColorSensor.area.color` 또는 직접 연결된 `ColorArea.color`를 읽어 이 classifier에 전달한다.

필수 API:

```csharp
public class ColorClassifier : MonoBehaviour, IColorClassifier
{
    public ColorClassificationResult Classify(Color sensedColor);
}
```

기준 색상:

```text
Normal   = #3140DD
Abnormal = #E03636
```

판정 방식:

```text
blueDistance = RGB 거리(sensedColor, #3140DD)
redDistance  = RGB 거리(sensedColor, #E03636)

blueDistance < redDistance → Normal
redDistance < blueDistance → Abnormal
```

예외 처리:

- sensedColor가 defaultColor에 가까우면 `Unknown`, `reliable=false`
- blue/red 거리 차이가 너무 작으면 `Unknown`, `reliable=false`
- B는 `reliable=false`면 재검사하거나 mission fail 처리

### 3) ResourceLockManager 구현

자원별 mutex를 관리한다.

필수 API:

```csharp
public class ResourceLockManager : MonoBehaviour, IResourceLockManager
{
    public bool TryAcquire(ResourceKey key, int robotId, int taskId, out ResourceLockToken token);
    public void Release(ResourceLockToken token);
    public bool IsLocked(ResourceKey key);
}
```

Lock 대상:

| ResourceKey          | 의미                            |
| -------------------- | ------------------------------- |
| `(Conveyor, 1~10)`   | 컨베이어별 Pick 작업 공간       |
| `(NormalBox, 100)`   | Normal Box 앞 Place 작업 공간   |
| `(AbnormalBox, 101)` | Abnormal Box 앞 Place 작업 공간 |
| `(CentralZone, 0)`   | 로봇 간 중앙 교차 구역          |
| `(RobotArmZone, id)` | 같은 station 또는 인접 station의 팔 작업 공간 |

Lock 규칙:

- 같은 `ResourceKey`는 동시에 한 robot만 획득 가능
- 획득 성공 시 token 반환
- 미션 종료/실패 시 반드시 Release
- 일정 시간 이상 lock 보유 시 warning 로그
- 같은 자원을 중복 획득하려는 경우 실패 처리
- lock 획득 실패 시 무한 대기하지 않도록 B와 timeout 정책을 맞춘다.
- 팔 동작이 겹칠 수 있는 구간에서는 station lock과 별도로 `RobotArmZone` lock을 사용한다.

### 4) PathPlanner / DeadlockGuard 구현

초기 구현은 복잡한 경로계획보다 단순 정책으로 충분하다.

기본 정책:

```text
RobotA: Conveyor 1~5 우선
RobotB: Conveyor 6~10 우선

반대편으로 이동해야 하는 경우 CentralZone lock 필요.
Box 접근은 NormalBox/AbnormalBox lock 필요.
팔 작업 공간이 겹칠 수 있는 경우 RobotArmZone lock 필요.
lock 획득 순서는 항상 CentralZone → Conveyor/Box → RobotArmZone 순으로 통일.
```

필수 API:

```csharp
public class PathPlanner : MonoBehaviour, IPathPlanner
{
    public bool RequiresCentralZone(int robotId, int fromStationId, int toStationId);
}
```

Deadlock 방지:

- lock을 여러 개 잡을 경우 순서를 통일한다.
- `WaitingForLock` 상태가 일정 시간 이상 지속되면 mission fail 또는 재계획한다.
- lock 획득 실패 시 무한 대기하지 않는다.
- RobotArmZone은 팔이 실제로 approach/action/retract 동작을 수행하는 짧은 구간에만 보유한다.
- CentralZone lock을 오래 보유하지 않도록 이동 완료 후 즉시 해제하는 정책을 우선한다.
- box lock과 conveyor lock을 동시에 오래 들고 있지 않도록 B의 MissionExecutor와 호출 순서를 맞춘다.

### 5) TelemetryLogger 구현

성능 최적화와 오류 분석용 로그를 남긴다.

필수 로그:

- task 생성/할당/완료
- queue length 변화
- robot state 변화
- lock acquire/release/fail
- grip success/fail
- color classification 결과
- slot reserve/commit/release
- mission result
- timeout/stuck
- 최종 완료 시간

주의:

- TelemetryLogger는 점수 조작용이 아니라 디버깅/분석용이다.
- 각 슬라이스가 공통 logger를 호출할 수 있도록 인터페이스를 유지한다.
- 로그 포맷은 통합 테스트 때 원인 분석이 가능할 정도로 일관되게 유지한다.

## D-4. 다른 슬라이스와의 연결 스키마

### D → 전체: Scene reference 제공

```csharp
StudentSceneReferences refs;
```

D는 Bootstrap 단계에서 RobotA/B, gripper, colorArea, environmentInfo 등을 연결해둔다.  
A/B/C는 직접 scene을 뒤지는 방식보다 `StudentSceneReferences`를 통해 필요한 참조를 받는다.

### B → D: 색상 판정

```csharp
Color sensed = colorArea.color;
ColorClassificationResult result = colorClassifier.Classify(sensed);
```

D는 B에게 다음 값을 보장해야 한다.

- `result`: `ClassificationResult.Normal` / `ClassificationResult.Abnormal` / `ClassificationResult.Unknown`
- `reliable`: 판정 신뢰 여부
- `blueDistance`, `redDistance`: 디버깅용 거리 값
- `message`: defaultColor, ambiguous 등 사유

### B → D: CentralZone 필요 여부 확인

```csharp
bool needCentral = pathPlanner.RequiresCentralZone(
    robotId,
    fromStationId,
    toStationId
);
```

B는 이 결과에 따라 CentralZone lock을 먼저 획득할지 결정한다.

### B → D: Lock 획득

```csharp
bool ok = lockManager.TryAcquire(
    new ResourceKey(LockResourceType.Conveyor, conveyorId),
    robotId,
    taskId,
    out var token
);
```

B는 lock 획득 실패 시 해당 자원에 바로 접근하지 않는다.

### B → D: Lock 해제

```csharp
lockManager.Release(token);
```

중요:

- 성공/실패/timeout 모든 경로에서 lock을 해제해야 한다.
- try/finally에 해당하는 구조로 구현하는 것을 권장한다.
- slot 예약 실패나 grip 실패가 발생해도 이미 획득한 lock은 반드시 해제한다.

### A/B/C → D: 로그 기록

```csharp
logger.LogMessage("Grip", "RobotA grip failed: no candidate");
logger.LogMissionResult(result);
logger.LogLock("Acquire", key, robotId, taskId);
```

## D-5. 종속성

| 대상 슬라이스 | 종속 내용                                          | 작업 순서                                                             |
| ------------- | -------------------------------------------------- | --------------------------------------------------------------------- |
| Slice A       | task/mission 로그, 공통 스키마, scene reference    | A의 common schema 필요                                                |
| Slice B       | MissionExecutor가 Lock/PathPlanner/Classifier 호출 | D의 LockManager/PathPlanner/ColorClassifier가 B 통합 전 준비되어야 함 |
| Slice C       | slot 로그 수집, box lock과 slot 예약 순서 확인     | C의 Palletizer 결과를 로그로 수집                                     |

## D-6. 작업 순서

1. `StudentSceneReferences` / `StudentBootstrap` skeleton 작성
2. `ColorClassifier` 작성
3. 임의 Color 값으로 Normal/Abnormal 판정 단위 테스트
4. `ResourceLockManager` 작성
5. 같은 key 중복 acquire 방지 테스트
6. `PathPlanner` 작성
7. `TelemetryLogger` 작성
8. B의 MissionExecutor에 Lock/PathPlanner/Classifier 연결
9. 중앙 구역 lock 정책 추가
10. 2대 로봇 동시 실행 시 lock 대기/해제 로그 점검
11. Deadlock timeout 정책 추가

## D-7. 완료 기준

- [ ] `StudentSceneReferences`에 필요한 scene reference 항목이 정의되어 있다.
- [ ] Inspector에서 RobotA/B, EnvironmentInfo, Gripper, ColorSensor/ColorArea reference를 연결할 수 있다.
- [ ] `#3140DD`에 가까운 색상을 Normal로 판정한다.
- [ ] `#E03636`에 가까운 색상을 Abnormal로 판정한다.
- [ ] defaultColor 또는 애매한 색상을 `reliable=false`로 처리한다.
- [ ] 같은 conveyor lock을 두 로봇이 동시에 획득할 수 없다.
- [ ] NormalBox/AbnormalBox lock이 동작한다.
- [ ] CentralZone lock 필요 여부를 판단할 수 있다.
- [ ] lock 획득 실패 시 무한대기하지 않는다.
- [ ] 모든 mission 종료 경로에서 lock이 해제된다.
- [ ] 충돌/Deadlock 관련 로그를 남긴다.
- [ ] task, mission, lock, classification 로그를 남긴다.

---

## 5. 슬라이스 간 종속성 정리

### 5.1 전체 종속성 표

| 기능                  | 주 담당        | 의존 대상 | 먼저 필요한 것                                                                          |
| --------------------- | -------------- | --------- | --------------------------------------------------------------------------------------- |
| 공통 스키마           | A              | 전체      | 가장 먼저 확정                                                                          |
| Scene reference       | A/D            | B/C/D     | RobotA/B, env, gripper, colorArea 연결                                                  |
| Queue 읽기            | A              | 없음      | IEnvironmentInfo 연결                                                                   |
| Task 할당             | A              | B         | RobotAgent interface                                                                    |
| Robot mission         | B              | A/C/D     | MissionRequest, PoseProvider, LockManager, Classifier                                   |
| Pick pose             | C              | B         | B가 실제 로봇으로 테스트                                                                |
| Grip 조건             | B              | C         | pick pose 정확도                                                                        |
| Color classification  | D              | B         | B가 `ColorSensor.area.color` 또는 `ColorArea.color` 전달                                |
| Box slot              | C              | B/D       | B가 slot 요청, D가 box lock 제공                                                        |
| Resource lock         | D              | B/A       | B가 lock 사용                                                                           |
| Telemetry             | D              | A/B/C     | 각 슬라이스가 logger 호출                                                               |
| 성능 최적화           | 전체           | 전체      | 통합 후 반복 테스트                                                                     |
| 통합 QA / 빌드 / 제출 | 공통 후반 작업 | 전체      | 개발 슬라이스 기능 완료 후 compile error, scene reference, 빌드, 영상, 보고서, zip 검증 |

### 5.2 반드시 지켜야 할 호출 순서

#### 작업 할당 순서

```text
A: queue snapshot 생성
A: TaskAllocator로 WorkTask 선택
A: conveyor reservation 설정
A: MissionRequest 생성
A → B: StartMission(request)
```

#### Pick 순서

```text
B → D: PathPlanner로 CentralZone 필요 여부 확인
B → D: 필요 시 CentralZone lock 획득
B → D: conveyor lock 획득
B: GoToOperatingStation(conveyorId)
B → C: GetConveyorPickPose(conveyorId)
B: approachPos 이동
B: actionPos 이동
B: grasp ready 대기
B: TryGrip()
B: IsHolding 확인
B: retractPos 이동
```

#### Classification 순서

```text
B: 물품을 들고 안정화
B: ColorSensor.area.color 또는 ColorArea.color 읽기
B → D: ColorClassifier.Classify(color)
D → B: ColorClassificationResult 반환
B: ClassificationResult를 BoxType으로 변환해 station 100/101 결정
```

#### Place 순서

```text
B → D: box lock 획득
B: GoToOperatingStation(100 또는 101)
B → C: ReserveNextSlot(boxType, robotId, taskId)
B: slot.approachPos 이동
B: slot.placePos 이동
B: Release()
B → C: CommitSlot(taskId)
B → D: box lock 해제
B → A: MissionResult 반환
```

#### 실패 처리 순서

```text
실패 발생
→ 들고 있는 물품이 있으면 안전 처리
→ 예약한 slot이 있으면 ReleaseSlot(taskId)
→ 획득한 lock 전부 Release
→ MissionResult(success=false, failureReason=...) 생성
→ A callback 호출
→ A가 task retry 또는 fail 처리
```

---

## 6. 4인 개발 일정 권장안

### 1단계: 공통 스키마 및 환경 연결

목표: 각자 개발 가능한 기반 만들기

| 담당 | 작업                                                                                     |
| ---- | ---------------------------------------------------------------------------------------- |
| A    | Common schema, EnvironmentScanner, queue 출력                                            |
| B    | RobotAgent skeleton, StartMission skeleton                                               |
| C    | PoseTable skeleton, conveyor 1 임시 pose                                                 |
| D    | ColorClassifier skeleton, ResourceLockManager skeleton, StudentSceneReferences 연결 지원 |

완료 조건:

- compile error 0개
- queue length 출력 가능
- RobotA가 station 1로 이동 가능
- 공통 interface가 고정됨

### 2단계: 단일 로봇 Vertical Slice

목표: RobotA가 물품 1개를 처리

| 담당 | 작업                                             |
| ---- | ------------------------------------------------ |
| A    | conveyor 1에 대해 WorkTask 생성, RobotA에 전달   |
| B    | RobotA 이동 → pick → release 흐름 구현           |
| C    | conveyor 1 pick pose, Normal Box place pose 보정 |
| D    | 임시 color classify, lock acquire/release 로그   |

완료 조건:

- RobotA가 컨베이어 1에서 물품 1개를 집음
- Normal Box에 낮은 높이로 놓음
- MissionResult가 A로 돌아옴

### 3단계: 색상 분류 및 양쪽 박스 처리

목표: Normal/Abnormal 분류 동작

| 담당 | 작업                                                |
| ---- | --------------------------------------------------- |
| A    | 작업 재시도/실패 상태 반영                          |
| B    | InspectProduct 상태 추가                            |
| C    | Abnormal Box place pose/slot 추가                   |
| D    | ColorSensor/ColorArea 기반 거리 판정, defaultColor 재검사 |

완료 조건:

- 파란색은 Normal Box
- 빨간색은 Abnormal Box
- 분류 실패 시 재검사 또는 MissionResult 실패 반환

### 4단계: 2대 로봇 병렬 처리

목표: RobotA/B가 동시에 다른 작업 처리

| 담당 | 작업                                             |
| ---- | ------------------------------------------------ |
| A    | RobotA/B idle 감지, 중복 배정 방지               |
| B    | RobotB도 동일 mission 실행 가능하게 구성         |
| C    | 1~10번 conveyor pose 확장, box slot 확장         |
| D    | conveyor/box/central lock 적용, deadlock timeout |

완료 조건:

- 같은 conveyor에 중복 접근하지 않음
- 같은 box 앞에 동시에 접근하지 않음
- 충돌 없이 2대 로봇이 동작

### 5단계: 성능 최적화 및 제출

목표: 시간/미적재/분류오류/충돌 최소화

| 담당 | 작업                                                                  |
| ---- | --------------------------------------------------------------------- |
| A    | 우선순위 점수 튜닝, 빠른 컨베이어 선제 처리                           |
| B    | mission timeout, grip retry, 불필요한 이동 제거                       |
| C    | slot 정렬 개선, place 높이 안정화                                     |
| D    | lock 대기 시간 분석, 로그 정리, central lock/deadlock 안정화          |
| 공통 | 통합 QA, Windows 빌드, 시연 영상, 발표자료, 보고서, 프로젝트 zip 검증 |

완료 조건:

- 180초 전체 시뮬레이션 반복 테스트 완료
- 분류 오류 0개 목표
- 충돌 0회 목표
- 64개 전체 또는 최대한 많은 물품 처리
- Windows 빌드와 프로젝트 zip 검증

---

## 7. 브랜치 / PR 운영 권장

### 7.1 브랜치 이름

```text
feat/common-schema
feat/fleet-scheduling
feat/robot-mission
feat/pose-palletizer
feat/vision-safety-telemetry-bootstrap
fix/integration-robotA-single-task
fix/integration-two-robots
```

### 7.2 PR 순서

1. `feat/common-schema`
2. `feat/robot-mission` skeleton
3. `feat/pose-palletizer` skeleton
4. `feat/vision-safety-telemetry-bootstrap` skeleton
5. `feat/fleet-scheduling`
6. integration PR

### 7.3 Unity scene 충돌 방지

- scene 파일은 D 또는 팀장만 수정한다.
- 나머지는 script 파일 위주로 PR을 올린다.
- reference 연결이 필요한 경우 D에게 요청한다.
- `[LOCKED] BaseAssets` 변경이 PR에 포함되면 merge하지 않는다.

---

## 8. 팀원별 최종 산출물 체크리스트

### Slice A 체크리스트

- [ ] Common schema 작성
- [ ] `EnvironmentScanner` 작성
- [ ] `FleetManager` 작성
- [ ] `TaskAllocator` 작성
- [ ] queue length polling 동작
- [ ] WorkTask 생성/배정 동작
- [ ] MissionResult callback 처리
- [ ] task retry/fail 처리
- [ ] 완료 시간 및 처리 개수 집계

### Slice B 체크리스트

- [ ] `RobotAgent` 작성
- [ ] `MissionExecutor` 작성
- [ ] `GripperAdapter` 작성
- [ ] RobotA 단일 미션 성공
- [ ] RobotB 단일 미션 성공
- [ ] `GoToOperatingStation()` 호출 흐름 동작
- [ ] `MoveArmTo()`로 approach/action/retract 동작
- [ ] `TryGrip()` 성공 확인
- [ ] `IsHolding` 확인
- [ ] `Release()` 후 안정 배치
- [ ] MissionResult 반환

### Slice C 체크리스트

- [ ] `PoseTable` 작성
- [ ] `CalibrationManager` 작성
- [ ] `Palletizer` 작성
- [ ] conveyor 1~10 pick pose 제공
- [ ] Normal Box slot 제공
- [ ] Abnormal Box slot 제공
- [ ] slot reserve/commit/release 동작
- [ ] 자유낙하 없는 place 높이 보정
- [ ] 박스 밖 이탈 최소화
- [ ] 정렬도 개선

### Slice D 체크리스트

- [ ] `StudentBootstrap` 작성
- [ ] `StudentSceneReferences` 작성
- [ ] Inspector에서 RobotA/B, EnvironmentInfo, Gripper, ColorSensor/ColorArea reference 연결 가능
- [ ] `ColorClassifier` 작성
- [ ] `ResourceLockManager` 작성
- [ ] `PathPlanner` 작성 또는 단순 정책 구현
- [ ] `DeadlockGuard` 작성 또는 timeout 정책 구현
- [ ] `TelemetryLogger` 작성
- [ ] ColorSensor/ColorArea 색상 판정 동작
- [ ] defaultColor/ambiguous 처리
- [ ] conveyor lock 동작
- [ ] box lock 동작
- [ ] central lock 동작
- [ ] deadlock 방지
- [ ] task/mission/lock/classification 로그 기록

---

## 9. 통합 테스트 시나리오

### Test 1. Queue 읽기

목표: A 단독 검증

```text
씬 실행
→ CurrentTime 출력
→ GetQueueLength(1~10) 출력
→ 시간이 지나며 큐 변화 확인
```

성공 기준:

- 1~10번 큐 길이가 정상 출력된다.
- 큐가 0인 컨베이어는 작업 후보에서 제외된다.

### Test 2. RobotA 단일 이동

목표: B 기본 이동 검증

```text
RobotA.StartMission(conveyorId=1)
→ GoToOperatingStation(1)
→ GoToOperatingStation(100)
```

성공 기준:

- RobotA가 컨베이어 1로 이동한다.
- RobotA가 Normal Box station으로 이동한다.

### Test 3. RobotA Pick

목표: B/C grip 검증

```text
RobotA station 1 이동
→ C의 pick pose 요청
→ approach/action/retract
→ TryGrip
```

성공 기준:

- `CurrentCandidate != null`
- `IsGraspReady == true`
- `TryGrip() == true`
- `IsHolding == true`

### Test 4. Color 분류

목표: D classifier 검증

```text
ColorSensor.area.color 또는 ColorArea.color 읽기
→ ColorClassifier.Classify()
→ Normal/Abnormal 판정
```

성공 기준:

- 파란색은 Normal
- 빨간색은 Abnormal
- defaultColor는 reliable=false

### Test 5. Place / Palletizing

목표: C palletizer 검증

```text
classification 결과에 따라 station 결정
→ box lock 획득
→ Palletizer.ReserveNextSlot(boxType, robotId, taskId)
→ placePos 이동
→ Release
→ CommitSlot
```

성공 기준:

- 물품이 박스 안에 남는다.
- slot index가 증가한다.
- 자유낙하가 없다.

### Test 6. RobotA/B 동시 작업

목표: A/B/D 통합 검증

```text
A가 RobotA/B idle 감지
→ 서로 다른 conveyor 할당
→ D lock 적용
→ 두 로봇 동시 처리
```

성공 기준:

- 같은 conveyor 중복 배정 없음
- 같은 box 동시 접근 없음
- 충돌 없음
- deadlock 없음

### Test 7. 전체 180초 시뮬레이션

목표: 최종 성능 검증

```text
씬 시작
→ 생산 종료까지 자동 처리
→ 64개 처리 여부 확인
→ 분류 오류/충돌/미적재/완료 시간 기록
```

성공 기준:

- 분류 오류 0개 목표
- 충돌 0회 목표
- 미적재 최소화
- 가능한 한 250초 이하 완료

---

## 10. 최종 통합 구조 요약

최종 구조는 **파일/폴더 구조**와 **실행 시 의존성 구조**를 구분해서 이해한다.

---

### 10.1 실제 파일/폴더 구조

모든 학생 코드는 아래 경로에 작성한다.

```text
Assets/CPS_IC_PBL_2026/Scripts/Student/
├── Common/
│   ├── StudentEnums.cs
│   ├── StudentSchemas.cs
│   ├── StudentInterfaces.cs
│   └── StudentConstants.cs
│
├── Bootstrap/
│   ├── StudentBootstrap.cs
│   └── StudentSceneReferences.cs
│
├── Fleet/
│   ├── FleetManager.cs
│   ├── EnvironmentScanner.cs
│   └── TaskAllocator.cs
│
├── Robot/
│   ├── RobotAgent.cs
│   ├── MissionExecutor.cs
│   └── GripperAdapter.cs
│
├── Pose/
│   ├── PoseTable.cs
│   ├── CalibrationManager.cs
│   └── Palletizer.cs
│
├── Vision/
│   └── ColorClassifier.cs
│
├── Safety/
│   ├── ResourceLockManager.cs
│   ├── PathPlanner.cs
│   └── DeadlockGuard.cs
│
└── Telemetry/
    └── TelemetryLogger.cs
```

각 폴더의 의미는 다음과 같다.

| 폴더        | 담당 역할                                                   |
| ----------- | ----------------------------------------------------------- |
| `Common`    | 모든 슬라이스가 공유하는 enum, schema, interface, constant  |
| `Bootstrap` | 씬 참조 연결, 전체 시스템 초기화                            |
| `Fleet`     | 큐 감시, 작업 생성, 우선순위 계산, RobotA/B 작업 할당       |
| `Robot`     | 로봇 1대의 상태 머신, 이동, Pick, Grip, Inspect, Place 실행 |
| `Pose`      | 컨베이어별 Pick 좌표, 박스 Place 좌표, 팔레타이징 slot 관리 |
| `Vision`    | `ColorSensor.area.color` 또는 `ColorArea.color` 기반 Normal/Abnormal 분류 |
| `Safety`    | Conveyor/Box/CentralZone lock, 충돌 회피, deadlock 방지     |
| `Telemetry` | 작업, lock, grip, classification, mission result 로그 기록  |

---

### 10.2 4인 역할과 코드 위치 매핑

| 팀원   | 담당 슬라이스                                    | 주 담당 폴더                                     |
| ------ | ------------------------------------------------ | ------------------------------------------------ |
| 팀원 1 | Slice A: Fleet / Scheduling / Common Schema      | `Common/`, `Fleet/`                              |
| 팀원 2 | Slice B: Robot Mission / Motion / Gripper        | `Robot/`                                         |
| 팀원 3 | Slice C: Pose / Calibration / Palletizing        | `Pose/`                                          |
| 팀원 4 | Slice D: Vision / Safety / Telemetry / Bootstrap | `Vision/`, `Safety/`, `Telemetry/`, `Bootstrap/` |

단, `Bootstrap/StudentSceneReferences.cs`는 코드 자체는 공통 성격이 있지만, Unity Inspector reference 연결은 팀원 4 또는 팀장이 단독으로 관리한다.

---

### 10.3 실행 시 의존성 구조

실행 중 객체 호출 흐름은 아래와 같다.

```text
StudentBootstrap
  └─ StudentSceneReferences
       ├─ RobotA Controller
       ├─ RobotB Controller
       ├─ EnvironmentInfo
       ├─ RobotA Gripper
       ├─ RobotB Gripper
       ├─ RobotA ColorSensor/ColorArea
       └─ RobotB ColorSensor/ColorArea

FleetManager
  ├─ EnvironmentScanner
  │    └─ IEnvironmentInfo
  │
  ├─ TaskAllocator
  │    └─ ConveyorSnapshot / StudentRobotSnapshot
  │
  ├─ RobotAgent A
  │    └─ MissionExecutor A
  │
  └─ RobotAgent B
       └─ MissionExecutor B
```

`FleetManager`는 직접 로봇 팔을 움직이지 않는다.  
`FleetManager`는 큐 상태를 보고 `MissionRequest`를 만들고, `RobotAgent`에게 작업을 넘기는 역할만 한다.

---

### 10.4 MissionExecutor의 실행 의존성

`MissionExecutor`는 물품 1개를 실제로 처리하는 중심 실행기다.

```text
MissionExecutor
  ├─ IRobotController
  │    ├─ GoToOperatingStation()
  │    └─ MoveArmTo()
  │
  ├─ GripperAdapter
  │    └─ SuctionGripper
  │         ├─ IsGraspReady
  │         ├─ TryGrip()
  │         ├─ IsHolding
  │         └─ Release()
  │
  ├─ IPoseProvider
  │    └─ PoseTable
  │         └─ GetConveyorPickPose()
  │
  ├─ IPalletizer
  │    └─ Palletizer
  │         ├─ ReserveNextSlot(BoxType)
  │         ├─ CommitSlot()
  │         └─ ReleaseSlot()
  │
  ├─ IColorClassifier
  │    └─ ColorClassifier
  │         └─ Classify(ColorSensor.area.color 또는 ColorArea.color)
  │
  ├─ IResourceLockManager
  │    └─ ResourceLockManager
  │         ├─ TryAcquire()
  │         └─ Release()
  │
  ├─ IPathPlanner
  │    └─ PathPlanner
  │         └─ RequiresCentralZone()
  │
  └─ ITelemetryLogger
       └─ TelemetryLogger
```

---

### 10.5 최종 실행 플로우

```text
1. StudentBootstrap이 씬 참조를 연결한다.
2. FleetManager가 EnvironmentScanner로 1~10번 queue length를 읽는다.
3. TaskAllocator가 가장 급한 컨베이어를 선택한다.
4. FleetManager가 idle 상태의 RobotAgent에게 MissionRequest를 전달한다.
5. RobotAgent가 MissionExecutor를 실행한다.
6. MissionExecutor가 PathPlanner로 CentralZone lock 필요 여부를 확인한다.
7. 필요하면 ResourceLockManager로 CentralZone lock을 획득한다.
8. MissionExecutor가 ResourceLockManager로 Conveyor lock을 획득한다.
9. MissionExecutor가 IRobotController로 해당 station에 이동한다.
10. MissionExecutor가 PoseTable에서 pick pose를 받아 팔을 이동한다.
11. GripperAdapter가 SuctionGripper로 물품을 집는다.
12. MissionExecutor가 ColorSensor.area.color 또는 ColorArea.color를 읽고 ColorClassifier에 분류를 요청한다.
13. 분류 결과에 따라 Normal Box 또는 Abnormal Box station을 결정한다.
14. MissionExecutor가 Box lock을 획득한다.
15. MissionExecutor가 Palletizer에서 다음 slot을 예약한다.
16. MissionExecutor가 place pose로 이동해 낮은 높이에서 Release한다.
17. Place 성공 시 Palletizer.CommitSlot(taskId)를 호출한다.
18. 모든 lock을 해제한다.
19. MissionExecutor가 MissionResult를 RobotAgent/FleetManager에 반환한다.
20. FleetManager는 task 상태를 갱신하고 다음 작업을 배정한다.
```

---

### 10.6 최종 구조에서 지켜야 할 원칙

- `FleetManager`는 scheduling만 담당한다.
- `RobotAgent / MissionExecutor`는 작업 1개를 실제로 수행한다.
- `PoseTable / Palletizer`는 좌표만 책임진다.
- `ColorClassifier`는 색상 판정만 책임진다.
- `ResourceLockManager`는 자원 점유만 책임진다.
- `PathPlanner / DeadlockGuard`는 중앙 이동 구역과 deadlock 방지를 책임진다.
- `TelemetryLogger`는 로그만 책임진다.
- `StudentBootstrap`은 씬 참조 연결과 초기화만 책임진다.
- `[LOCKED] BaseAssets`는 직접 수정하지 않는다.

---

## 11. 최종 주의사항

1. `IRobotController`는 이동 API 중심이다. Grip, 색상 검사, slot 계산은 학생 코드에서 조합해야 한다.
2. `RealProduct.isNormal` 직접 접근은 금지한다.
3. `[LOCKED] BaseAssets` 원본은 수정하지 않는다.
4. `ColorArea.color`가 defaultColor이면 바로 판정하지 말고 재검사한다. ColorSensor를 참조하는 경우에도 실제 색상값은 `ColorSensor.area.color`다.
5. `TryGrip()`은 접촉 조건이 맞아야 성공한다. pose 보정 없이 호출만 반복하면 해결되지 않는다.
6. 두 로봇이 같은 conveyor/box에 동시에 접근하지 않도록 lock을 사용한다.
7. slot 예약 후 place 실패 시 반드시 `ReleaseSlot(taskId)`를 호출한다.
8. lock 획득 후 실패/timeout이 발생해도 반드시 lock을 해제한다.
9. Unity scene reference는 한 명이 관리한다.
10. 4명이 각자 개발하더라도 `Common` 스키마는 임의로 바꾸지 않는다.

---

## 12. 공통 후반 작업: Integration / QA / Build / Submission

QA, 빌드, 영상, 보고서, zip 제출은 특정 개발 슬라이스의 단독 책임이 아니라 **공통 후반 작업**으로 관리한다.  
Slice D는 scene reference와 로그를 제공하지만, 최종 제출물 전체를 단독 책임지지 않는다.

| 항목                                 | 주 담당                  | 참여 |
| ------------------------------------ | ------------------------ | ---- |
| 통합 테스트 계획                     | 팀장 또는 PM             | 전체 |
| Compile error 0개 확인               | 각자 본인 코드 우선 확인 | 전체 |
| Scene reference missing 확인         | Slice D 또는 팀장        | 전체 |
| `[LOCKED] BaseAssets` 수정 여부 확인 | 팀장                     | 전체 |
| 180초 전체 시뮬레이션 반복 테스트    | 팀장 또는 PM             | 전체 |
| 성능 로그 정리                       | Slice A + Slice D        | 전체 |
| 분류 오류/충돌/미적재 확인           | 전체                     | 전체 |
| Windows Standalone 빌드              | 팀장 또는 빌드 담당자    | 전체 |
| 3배속 시연 영상 촬영                 | 발표/영상 담당자         | 전체 |
| 발표자료 PPTX                        | 발표 담당자              | 전체 |
| 최종 보고서                          | 문서 담당자              | 전체 |
| Unity 프로젝트 zip                   | 팀장 또는 빌드 담당자    | 전체 |
| 제출 전 최종 검증                    | 팀장 또는 PM             | 전체 |

공통 후반 작업은 기능 구현이 어느 정도 끝난 뒤 시작하지만, 로그 포맷과 scene reference 관리는 개발 초반부터 맞춰둔다.

---

## 13. 역할 분배 최종안

| 팀원   | 최종 담당                                        | 핵심 산출물                                                                                                                            |
| ------ | ------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------- |
| 팀원 1 | Slice A: Fleet / Scheduling / Common Schema      | Common schema, FleetManager, TaskAllocator, EnvironmentScanner                                                                         |
| 팀원 2 | Slice B: Robot Mission / Motion / Gripper        | RobotAgent, MissionExecutor, GripperAdapter                                                                                            |
| 팀원 3 | Slice C: Pose / Calibration / Palletizing        | PoseTable, CalibrationManager, Palletizer, slot 좌표                                                                                   |
| 팀원 4 | Slice D: Vision / Safety / Telemetry / Bootstrap | ColorClassifier, ResourceLockManager, PathPlanner, DeadlockGuard, TelemetryLogger, StudentBootstrap, StudentSceneReferences, 제출 검증 |

이 분배의 핵심은 다음이다.

- 팀원 1은 “무슨 작업을 누구에게 시킬지”를 담당한다.
- 팀원 2는 “로봇이 작업 하나를 실제로 수행하는 과정”을 담당한다.
- 팀원 3은 “로봇 팔이 어디로 가야 하는지와 박스 안 어디에 놓을지”를 담당한다.
- 팀원 4는 “분류, 충돌 방지, 로그, 씬 참조 연결, 초기화”를 담당한다.

각 슬라이스는 독립적으로 시작할 수 있지만, 최종 통합은 반드시 아래 순서로 한다.

```text
Common Schema
→ RobotA 단일 이동
→ RobotA 단일 Pick/Place
→ 색상 분류 연결
→ Palletizer slot 연결
→ RobotB 추가
→ Resource Lock 적용
→ 180초 전체 테스트
→ 성능 최적화
→ 공통 QA / 빌드 / 제출
```
