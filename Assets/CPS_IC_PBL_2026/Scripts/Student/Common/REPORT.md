# Common 구현 보고서

## 1. 구현 목적

Common 디렉터리는 CPS IC-PBL 2026 학생 구현 코드에서 모든 슬라이스가 함께 사용하는 공통 스키마, 인터페이스, 상수를 정의하는 영역이다. Slice A가 먼저 Common을 고정해야 Slice B, C, D가 같은 타입과 필드명을 기준으로 병렬 구현할 수 있다.

`Student/Common`은 `[LOCKED] BaseAssets/Common`을 대체하지 않고 학생 코드 내부 모듈 간 계약만 담당한다. `[LOCKED]`의 공식 타입인 `CPS.ICPBL.Common.ClassificationResult`와 `CPS.ICPBL.Common.BoxType`을 재사용하며, 기존 학생용 `ProductClass`는 제거했다.

이번 구현에서는 역할 분담 문서, 과제 설명 문서, 제공 API 설명, 로컬 `[LOCKED] BaseAssets` 코드를 기준으로 다음 4개 C# 파일을 작성했다.

```text
Assets/CPS_IC_PBL_2026/Scripts/Student/Common/
├── StudentEnums.cs
├── StudentSchemas.cs
├── StudentInterfaces.cs
└── StudentConstants.cs
```

Unity 협업 기준에 맞춰 각 C# 파일의 `.meta` 파일도 함께 추가했다.

## 2. 검토 기준

구현 전후로 다음 기준과 충돌 여부를 확인했다.

- 과제 설명: 2대의 RobotA/RobotB가 10개 컨베이어의 물품을 처리하고, 색상 센서 기반으로 Normal/Abnormal을 분류한 뒤 각 박스에 팔레타이징해야 한다.
- Notion 가이드: Unity 2022.3.x LTS 기반 프로젝트 구조와 `Assets/`, `Packages/`, `ProjectSettings/`가 있는 Unity 프로젝트 기준을 따른다.
- 제공 API 설명:
  - 로봇 이동은 `IRobotController.GoToOperatingStation`, `MoveBaseTo`, `MoveArmTo`를 통해 수행한다.
  - 환경 상태는 `IEnvironmentInfo.GetQueueLength`, `GetBoxOccupancy`, `CurrentTime`, `ProductionEndTime`, `NextProductionAt`을 통해 조회한다.
  - 색상 분류는 `ColorSensor.area.color` 또는 `ColorArea.color`를 읽어서 학생 코드가 직접 수행한다.
  - grip/release는 `SuctionGripper.TryGrip`, `Release`, `CanGrip(out reason)`을 통해 수행한다.
  - box slot 점유는 `BoxTrigger`의 `SlotCount`, `OccupiedSlotCount`, `IsSlotOccupied`, `RegisterSlotPlacement`를 기준으로 관리할 수 있다.
- 역할 분담 문서:
  - Slice A는 Common schema와 Fleet/Scheduling을 담당한다.
  - Slice B는 `MissionRequest`를 받아 로봇 미션을 수행하고 `MissionResult`를 반환한다.
  - Slice C는 `StationPose`, `BoxSlotPose`, `IPoseProvider`, `IPalletizer`를 구현한다.
  - Slice D는 `IColorClassifier`, `IResourceLockManager`, `IPathPlanner`, `ITelemetryLogger`를 구현한다.

## 3. 파일별 구현 내용

### 3.1 StudentEnums.cs

프로젝트 전체에서 공유하는 enum을 정의했다.

- `ProductClass`
  - 더 이상 사용하지 않는다.
  - 색상 분류 결과는 `[LOCKED] CPS.ICPBL.Common.ClassificationResult`를 사용한다.
  - 목적 박스와 Palletizer 입력은 `[LOCKED] CPS.ICPBL.Common.BoxType`을 사용한다.
- `RobotRuntimeState`
  - `Idle`, `Reserved`, `MovingToConveyor`, `Picking`, `Retracting`, `Inspecting`, `MovingToBox`, `Placing`, `Releasing`, `Completed`, `Failed`, `WaitingForLock`, `Stuck`
  - RobotAgent와 FleetManager가 로봇 상태를 공통으로 표현하는 데 사용한다.
- `TaskStatus`
  - `Pending`, `Reserved`, `Running`, `Completed`, `Failed`, `Cancelled`
  - Fleet의 작업 생성, 예약, 실행, 완료, 실패 상태 관리에 사용한다.
- `LockResourceType`
  - `Conveyor`, `NormalBox`, `AbnormalBox`, `CentralZone`, `RobotArmZone`
  - Safety의 resource lock key를 만들 때 사용한다.
  - `[LOCKED] ResourceType` 대체가 아니라 `CentralZone` / `RobotArmZone`까지 포함하는 학생용 확장 lock 타입이다.
- `MissionFailureReason`
  - `None`, `QueueEmpty`, `MoveTimeout`, `GripFailed`, `ClassificationFailed`, `BoxLockFailed`, `PlaceFailed`, `CollisionRisk`, `Unknown`
  - Robot mission 실패 사유를 Fleet와 Telemetry에 전달할 때 사용한다.

### 3.2 StudentSchemas.cs

슬라이스 간에 주고받을 데이터 모델을 정의했다.

- `ConveyorSnapshot`
  - 컨베이어 id, queue length, 생산 주기, 다음 생산 예정 시각, 마지막 배정 시각, 예약 여부를 가진다.
  - Fleet의 environment scan과 task allocation에 사용한다.
- `StudentRobotSnapshot`
  - `[LOCKED] Common`의 `RobotSnapshot`을 `baseSnapshot`으로 보관하고, 학생 runtime state, 현재 station, 현재 task id를 추가로 가진다.
  - Fleet가 RobotA/B 상태를 비교해 작업을 배정할 때 사용한다.
  - `[LOCKED] Common`의 `RobotSnapshot`과 이름 충돌을 피하면서 공식 snapshot 필드는 재정의하지 않는다.
- `WorkTask`
  - task id, conveyor id, assigned robot id, 생성/배정 시각, priority score, status, retry count, debug reason을 가진다.
  - Fleet 내부 scheduling 단위다.
- `MissionRequest`
  - task id, robot id, conveyor id, 요청 시각, timeout 값을 가진다.
  - Fleet가 RobotAgent에게 넘기는 미션 요청이다.
- `MissionResult`
  - task id, robot id, conveyor id, 성공 여부, `ClassificationResult` 분류 결과, 목적 station id, 실패 사유, 메시지, 시작/종료 시각을 가진다.
  - RobotAgent가 Fleet callback으로 반환하는 미션 결과다.
- `StationPose`
  - station id, `approachPos`, `actionPos`, `retractPos`, arm 이동 시간을 가진다.
  - Pose 담당자가 컨베이어 pick pose와 box base pose를 제공할 때 사용한다.
- `BoxSlotPose`
  - `BoxType`, station id, slot index, `approachPos`, `placePos`, `retractPos`, 예약 여부, 예약 task id를 가진다.
  - Palletizer가 박스 내부 slot별 place pose를 제공할 때 사용한다.
- `ColorClassificationResult`
  - `ClassificationResult`, sensed color, blue/red distance, reliable 여부, message를 가진다.
  - Vision의 색상 분류 결과를 Robot과 Telemetry에 전달할 때 사용한다.
- `ResourceKey`
  - lock 대상 type과 id를 가진 struct다.
  - dictionary key로 안전하게 사용할 수 있도록 `IEquatable<ResourceKey>`, `Equals`, `GetHashCode`, `==`, `!=`를 구현했다.
- `ResourceLockToken`
  - 획득한 resource key, robot id, task id, 획득 시각을 가진다.
  - Safety lock 획득/해제 흐름에 사용한다.

### 3.3 StudentInterfaces.cs

다른 슬라이스가 구현하거나 호출해야 하는 공통 interface를 정의했다.

- `IRobotAgent`
  - Fleet가 RobotA/B에 미션을 전달하기 위한 인터페이스다.
  - `RobotId`, `State`, `CanAcceptTask`, `StartMission()`을 제공한다.
- `ITaskAllocator`
  - Fleet가 컨베이어 snapshot, robot snapshot, pending task를 바탕으로 가장 적절한 `WorkTask`를 선택할 때 사용한다.
- `IPoseProvider`
  - Robot/MissionExecutor가 conveyor pick pose와 box base pose를 요청할 때 사용한다.
- `IPalletizer`
  - Robot/MissionExecutor가 box slot을 예약, 확정, 해제할 때 사용한다.
- `IColorClassifier`
  - Robot/MissionExecutor가 `ColorSensor.area.color` 또는 `ColorArea.color` 값을 전달해 Normal/Abnormal/Unknown 판정을 받을 때 사용한다.
- `IResourceLockManager`
  - MissionExecutor가 conveyor, box, central zone, arm zone lock을 획득/해제할 때 사용한다.
- `IPathPlanner`
  - MissionExecutor가 이동 전 CentralZone lock 필요 여부를 확인할 때 사용한다.
- `ITelemetryLogger`
  - Fleet, Robot, Safety가 task, mission result, lock, message 로그를 남길 때 사용한다.

### 3.4 StudentConstants.cs

여러 슬라이스에서 반복해서 사용할 상수를 정의했다.

- Robot id
  - `RobotAId = 0`
  - `RobotBId = 1`
  - 제공 API의 `IRobotController.RobotId` 기준과 맞췄다.
- Station id
  - conveyor: `1~10`
  - Normal Box: `100`
  - Abnormal Box: `101`
- Conveyor 설정
  - queue capacity: `3`
  - 생산 주기: `[15, 18, 20, 20, 30, 36, 45, 45, 60, 90]`
  - index 0은 사용하지 않고, conveyor id 1~10을 배열 index로 바로 사용할 수 있게 했다.
- 시간 기본값
  - production end time은 `[LOCKED] IEnvironmentInfo.ProductionEndTime`을 사용한다.
  - mission timeout, move timeout, lock timeout, grip ready timeout, arm move duration 기본값을 정의했다.
- 색상 기준
  - Normal 기준색: `#3140DD`
  - Abnormal 기준색: `#E03636`
  - default sensor color: `Color.white`
- helper
  - `IsConveyorId()`
  - `IsRobotId()`
  - `IsBoxStationId()`
  - `TryGetBoxType()`
  - `GetBoxStationId()`
  - `GetBoxLockType()`
  - `GetConveyorProductionPeriod()`

## 4. 충돌 점검 결과

현재 Common 구현은 문서와 다음 지점에서 일치한다.

- `CPS.ICPBL.Student` namespace 아래에 공통 타입을 정의했다.
- `StationPose` 필드명은 문서 전체 기준인 `actionPos`를 사용했다.
- `BoxSlotPose`는 `approachPos`, `placePos`, `retractPos`, `slotIndex`를 포함한다.
- `MissionRequest`는 Fleet에서 RobotAgent로 넘길 수 있는 task id, robot id, conveyor id, 요청 시각, timeout 값을 가진다.
- `MissionResult`는 Fleet가 성공/실패, `ClassificationResult` 분류 결과, 목적 station, 실패 사유를 받을 수 있게 구성했다.
- `ResourceKey`는 class가 아니라 struct이며 equality/hash를 구현했다.
- `LockResourceType`은 문서의 `Conveyor`, `NormalBox`, `AbnormalBox`, `CentralZone`, `RobotArmZone`을 모두 포함하며 `[LOCKED] ResourceType` 대체 타입이 아니다.
- RobotA/B 2대 제어 기준인 robot id `0`, `1`을 상수로 정의했다.
- Station id `1~10`, `100`, `101`과 컨베이어 생산 주기가 과제 설명과 일치한다.
- 색상 기준값 `#3140DD`, `#E03636`을 상수로 정의했다.
- Common 구현은 `RealProduct.isNormal` 같은 정답성 필드에 접근하지 않는다.
- Common 구현은 `[LOCKED] BaseAssets`를 수정하지 않는다.

검토 결과, 현재 Common 구현에서 과제 설명, 제공 API, 역할 분담 문서, 다른 슬라이스 README와 직접 충돌하는 항목은 확인되지 않았다.

## 5. 검증 내용

Common C# 파일 4개는 Roslyn C# compiler로 단독 컴파일 검증을 수행했고 성공했다.

검증 대상:

```text
StudentEnums.cs
StudentSchemas.cs
StudentInterfaces.cs
StudentConstants.cs
```

전체 Unity 프로젝트 빌드는 Unity가 생성한 csproj의 `project.assets.json` 상태에 의존해 일반 `dotnet build`로는 신뢰성 있게 확인하지 않았다. 최종 확인은 Unity Editor에서 프로젝트를 열어 Console compile error 여부를 보는 방식으로 수행하면 된다.

## 6. 후속 작업 연결

Common 구현 이후 각 슬라이스는 다음 방식으로 이어서 작업하면 된다.

- Slice A / Fleet
  - `ConveyorSnapshot`, `StudentRobotSnapshot`, `WorkTask`, `MissionRequest`, `MissionResult`, `IRobotAgent`, `ITaskAllocator`, `ITelemetryLogger`를 사용한다.
- Slice B / Robot
  - `IRobotAgent`를 구현하고, `MissionRequest`를 받아 미션 수행 후 `MissionResult`를 반환한다.
  - `IPoseProvider`, `IPalletizer`, `IColorClassifier`, `IResourceLockManager`, `IPathPlanner`를 interface로 호출한다.
- Slice C / Pose
  - `IPoseProvider`, `IPalletizer`를 구현한다.
  - `StationPose`, `BoxSlotPose`를 반환한다.
- Slice D / Vision, Safety, Telemetry
  - `IColorClassifier`, `IResourceLockManager`, `IPathPlanner`, `ITelemetryLogger`를 구현한다.
  - `ColorClassificationResult`, `ResourceKey`, `ResourceLockToken`을 사용한다.
