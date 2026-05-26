# Fleet 구현 보고서: EnvironmentScanner / TaskAllocator / FleetManager

## 1. 구현 목적

Slice A Fleet의 관측 계층인 `EnvironmentScanner`, 작업 선택 계층인 `TaskAllocator`, scheduling 실행 계층인 `FleetManager`를 구현했다.

- `EnvironmentScanner`는 `[LOCKED]` 환경 조회 API를 학생 계약인 `ConveyorSnapshot[]`으로 변환한다.
- `TaskAllocator`는 Fleet가 만든 미배정 `Pending` 작업 중 queue saturation deadline이 가장 이른 작업을 non-preemptive EDF-inspired 방식으로 선택한다.
- `FleetManager`는 task 생성, reservation 설정/해제, available robot 판단, `MissionRequest` 전달과 `MissionResult` 결과 처리를 담당한다.
- 실제 robot motion, classification, palletizing, Safety lock, logger 구현과 scene 통합은 다른 슬라이스 및 통합 단계에 남아 있다.

## 2. 검토 기준

구현 전후에 다음 파일과 책임 구분을 대조했다.

- `[LOCKED] BaseAssets/Common/Interfaces/IEnvironmentInfo.cs`
  - `GetQueueLength(int conveyorId)`
  - `NextProductionAt(int conveyorId)`
  - 환경 조회 공식 API이며, 원본은 수정하지 않는다.
- `[LOCKED] BaseAssets/Environment/EnvironmentInfo.cs`
  - 현재 `NextProductionAt()`은 `-1f`를 반환하는 stub임을 확인했다.
- `[LOCKED] BaseAssets/Environment/OperatingStations.cs`
  - 선택적으로 주입된 경우 공식 station base position을 거리 cost 계산에 사용한다.
- `Student/Common/StudentSchemas.cs`
  - `ConveyorSnapshot`, `StudentRobotSnapshot`, `WorkTask` 필드 구성을 따른다.
- `Student/Common/StudentInterfaces.cs`
  - `ITaskAllocator.SelectBestTask()`, `IRobotAgent`, `ITelemetryLogger` 계약을 그대로 사용한다.
- `Student/Common/StudentConstants.cs`
  - conveyor id 범위 `1~10`과 생산 주기 매핑을 재사용한다.
- `Student/Fleet/README.md`, `CPS_2026_RoleSlice.md`, `CPS_2026_TaskOverview.md`
  - Fleet는 queue polling과 scheduling을 담당하고, 색상 판정, 물리 동작, Safety lock을 직접 처리하지 않는다는 경계를 확인했다.

## 3. 구현 파일

```text
Assets/CPS_IC_PBL_2026/Scripts/Student/Fleet/
|- EnvironmentScanner.cs
|- EnvironmentScanner.cs.meta
|- TaskAllocator.cs
|- TaskAllocator.cs.meta
|- FleetManager.cs
`- FleetManager.cs.meta
```

문서 변경 파일은 `Student/Fleet/README.md`, `CPS_2026_RoleSlice.md`, `CPS_2026_TaskOverview.md`, 본 `REPORT.md`이다.

## 4. EnvironmentScanner 구현

### 4.1 공식 환경 API 주입

`EnvironmentScanner`는 생성자에서 `CPS.ICPBL.Common.IEnvironmentInfo`를 받는다. 참조가 없으면 즉시 `ArgumentNullException`을 발생시켜, 잘못 연결된 상태로 scan이 진행되지 않게 했다.

### 4.2 전체 conveyor snapshot 생성

`Scan()`은 `StudentConstants.MinConveyorId`부터 `MaxConveyorId`까지 순회하여 항상 10개의 `ConveyorSnapshot`을 반환한다.

각 snapshot의 필드는 다음처럼 채운다.

| `ConveyorSnapshot` 필드 | 값의 출처 |
| --- | --- |
| `conveyorId` | 순회 중인 station id `1~10` |
| `queueLength` | `[LOCKED] IEnvironmentInfo.GetQueueLength(conveyorId)` |
| `productionPeriod` | `StudentConstants.GetConveyorProductionPeriod(conveyorId)` |
| `nextProductionAt` | `[LOCKED] IEnvironmentInfo.NextProductionAt(conveyorId)` |
| `lastAssignedAt` | `FleetManager`가 전달할 마지막 배정 시각 사전 |
| `isReserved` | `FleetManager`가 전달할 예약 conveyor 집합 |

### 4.3 상태 소유권 분리

scanner는 queue와 다음 생산 시각을 읽는 변환기일 뿐, scheduling 상태를 직접 소유하지 않는다.

- reservation의 생성, 유지, 해제는 `FleetManager` 책임이다.
- 마지막 배정 시각의 갱신은 `FleetManager` 책임이다.
- `Scan()`은 전달받은 scheduling 메타데이터를 현재 관측 결과에 합쳐 snapshot으로 반환한다.

이 구조는 Fleet reservation과 Safety의 물리 lock을 분리한다. `EnvironmentScanner`는 `ResourceKey`, lock 획득/해제, 로봇 동작을 호출하지 않는다.

### 4.4 후보 선별 경계

scanner는 queue가 `0`인 conveyor도 관측 결과에 포함한다. queue가 없는 conveyor를 작업 후보에서 제외하는 정책은 `TaskAllocator`가 구현해야 한다.

현재 베이스 구현에서는 `NextProductionAt()`이 `-1f`를 반환할 수 있다. scanner는 공식 API 값을 변형하지 않고 snapshot에 기록한다.

## 5. TaskAllocator 구현

### 5.1 선택 계약

`TaskAllocator`는 `ITaskAllocator`를 구현하며, 다음 조건을 모두 만족하는 기존 `WorkTask`만 후보로 평가한다.

- `TaskStatus.Pending`이며 아직 robot에 할당되지 않은 task
- 같은 conveyor id의 유효한 `ConveyorSnapshot`이 존재하는 task
- queue가 `0`보다 크고 reservation되지 않은 conveyor의 task

후보가 없으면 `null`을 반환한다. allocator는 task를 새로 만들거나 reservation 상태를 변경하지 않는다.

### 5.2 정책 변경 판단

초기 구현은 queue length, 생산 주기, 거리와 대기 순서를 하나의 가중치 점수로 합산했다. 이 방식은 점수 계수에 따라 실제로 먼저 가득 찰 conveyor가 뒤로 밀릴 수 있고, overflow 방지라는 Fleet의 목적을 직접 표현하지 못한다.

따라서 CPU scheduling의 EDF(Earliest Deadline First)에서 착안하여, 각 conveyor의 queue가 capacity에 도달할 예상 시각을 deadline으로 정의하고 가장 이른 deadline을 먼저 선택하도록 변경했다. 이미 배정되어 실행 중인 mission은 중단할 수 없으므로, 대상은 미배정 `Pending` task로 제한하는 non-preemptive 방식이다.

이 방식이 현재 과제 구조에 더 적합한 이유는 다음과 같다.

- 과제에서 중요한 실패 위험은 빠른 conveyor의 queue overflow이며, saturation deadline은 이를 직접 비교한다.
- `ConveyorSnapshot`의 queue length, 생산 주기, `nextProductionAt`만으로 계산 가능하여 Common 계약을 확장하지 않는다.
- `Reserved`/`Running` task를 제외하므로 B 슬라이스의 실행 중 미션과 충돌하지 않는다.
- 임의 가중치 튜닝보다 선택 이유를 telemetry와 시연에서 설명하기 쉽다.

예를 들어 공식 생산 예정 시각이 없는 상태에서 queue `1`, period `15`인 conveyor는 `30`초 뒤 포화가 예상되고, queue `2`, period `90`인 conveyor는 `90`초 뒤 포화가 예상된다. 단순 queue length 우선과 달리 새 정책은 먼저 가득 찰 빠른 conveyor를 선택한다.

단, 이 구현은 두 대의 robot과 mission 수행 시간까지 분석하는 정식 실시간 EDF 스케줄러가 아니라 overflow를 줄이기 위한 EDF-inspired heuristic이다.

### 5.3 Deadline 계산 및 선택 순서

queue가 아직 가득 차지 않은 경우의 saturation deadline은 다음처럼 계산한다.

```text
estimatedSaturationDeadline =
    nextProductionAt + (slotsUntilFull - 1) * productionPeriod

fallback when NextProductionAt is unavailable =
    slotsUntilFull * productionPeriod
```

- queue capacity인 `3`에 도달한 후보는 deadline이 이미 도래한 것으로 보고 다른 후보보다 먼저 선택한다.
- 평가 대상인 non-full 후보 모두에서 `nextProductionAt`이 유효할 때만 공식 생산 예정 시각을 사용한다.
- 한 후보라도 공식 시각이 `-1f`이면 전 후보를 생산 주기 기반 상대 deadline으로 비교하여 절대 시각과 상대 시간을 섞지 않는다.
- deadline이 같으면 짧은 생산 주기, 공식 station 기준 가까운 거리, 작은 `lastAssignedAt`, 오래 생성된 task, 낮은 conveyor id 순으로 선택한다.
- `OperatingStations`가 주입되지 않으면 거리 tie-break cost는 `0`이다.

평가된 후보의 `priorityScore`에는 deadline이 이를수록 큰 값이 되도록 `-estimatedSaturationDeadline`을 기록한다. `debugReason`에는 정책명, 계산된 deadline, 공식 시각 사용 여부 또는 fallback, queue, 생산 주기, 거리 tie-break cost를 기록한다.

### 5.4 Non-Preemption과 allocator 책임 경계

현재 `[LOCKED]` 구현의 `NextProductionAt()`은 `-1f` stub이므로 실제 실행에서는 생산 주기 fallback deadline이 사용된다. 베이스 API가 추후 유효한 시각을 제공하면 동일한 학생 스키마와 allocator 코드가 공식 시각을 활용한다.

`TaskAllocator`는 `TaskStatus.Pending`이며 미배정인 task만 검토한다. `FleetManager`가 선택된 task를 `Reserved`/`Running`으로 전환하고 mission을 시작하면 allocator는 해당 작업을 다시 선택하거나 선점하지 않는다. 색상 분류, palletizer, Safety lock, 로봇 동작 API 역시 호출하지 않는다.

## 6. FleetManager 구현

### 6.1 Scene component와 의존성 주입

`FleetManager`는 Student 측에서 scene GameObject에 붙이는 `MonoBehaviour`이다. Inspector에서 공식 `[LOCKED]` `EnvironmentInfo`를 연결하면 `Awake()`에서 scanner와 allocator를 내부 생성한다. `OperatingStations`는 deadline 동률일 때 거리 비교를 활성화하는 경우에만 사용한다.

Slice B/D 코드가 아직 없는 상태를 허용하기 위해 다음 주입 API를 제공한다.

- `Configure(...)`: Bootstrap이 환경, station, RobotA/B `IRobotAgent`, `ITelemetryLogger`를 한 번에 연결
- `ConfigureEnvironment(...)`: 환경만 주입하여 polling 및 pending task 생성 검증
- `ConfigureRobotAgents(...)` / `RegisterRobotAgent(...)`: B 구현체가 생긴 뒤 dispatch 대상 추가
- `UpdateRobotSnapshot(...)`: 거리 tie-break를 사용할 통합 단계에서 실제 robot 위치 갱신

agent가 등록되지 않은 상태에서는 환경 관측과 pending task 생성까지만 수행하며, 임시 물리 동작이나 다른 슬라이스의 대체 구현을 Fleet에 넣지 않았다.

### 6.2 Task 및 reservation 소유

`RunSchedulingCycle()`은 다음 순서로 실행된다.

1. `EnvironmentScanner.Scan()`으로 최신 snapshot을 생성한다.
2. queue가 존재하고 active task가 없는 conveyor마다 하나의 `Pending WorkTask`를 만든다.
3. 아직 배정되지 않은 pending task의 queue가 비면 `Cancelled`로 종료한다.
4. 각 available agent에 대해 `TaskAllocator.SelectBestTask()`로 deadline이 가장 이른 task를 선택한다.
5. 선택 즉시 conveyor reservation과 `lastAssignedAt`을 기록하고 task를 `Reserved`, 이어서 `Running`으로 전환한다.
6. `MissionRequest`를 만들어 `IRobotAgent.StartMission()`에 전달한다.

한 conveyor에는 동시에 하나의 active task만 유지한다. 이를 통해 두 robot이 같은 conveyor를 중복 배정받지 않도록 Fleet 수준 reservation을 적용한다. 이는 D가 구현할 물리적 resource lock을 대체하지 않는다.

### 6.3 Callback, retry, non-preemption

`StartMission()` 호출 전 task 상태를 `Running`으로 바꾸므로, mission callback이 돌아오기 전에는 EDF allocator 후보로 다시 들어가지 않는다. 실행 중 더 이른 deadline의 task가 생겨도 진행 중 작업은 중단하지 않고, idle인 다른 robot 또는 이후 cycle에서 처리한다.

- 성공 결과: reservation 해제, task를 `Completed`로 전환, active conveyor 해제
- 실패 결과: reservation 해제, `retryCount` 증가, 기본 `maxRetryCount = 1` 범위 내이면 `Pending`으로 되돌려 재선택 허용
- retry 한도 초과: task를 `Failed`로 전환하고 active conveyor 해제
- 잘못된 callback 식별자 또는 `StartMission()` 예외: `MissionFailureReason.Unknown` 실패 결과로 정규화하여 reservation이 남지 않게 처리

`ITelemetryLogger`가 주입되면 task 생성, 배정, 결과 및 상태 메시지를 해당 계약으로 전달한다. D가 아직 없으면 선택적으로 `Debug.Log` fallback만 사용한다.

## 7. 명세 및 스키마 일치 점검

| 점검 항목 | 결과 |
| --- | --- |
| `[LOCKED] IEnvironmentInfo`를 통해 queue 상태를 읽는가 | 일치 |
| conveyor 범위가 과제 기준 `1~10`인가 | 일치 |
| 출력이 `ConveyorSnapshot[]` 계약을 따르는가 | 일치 |
| 생산 주기를 기존 `StudentConstants`에서 재사용하는가 | 일치 |
| reservation 상태를 Fleet 소유 상태로 유지하는가 | 일치 |
| queue `0` 및 reservation 후보 제외를 allocator가 처리하는가 | 일치 |
| capacity `3`인 queue가 즉시 deadline으로 최우선인가 | 일치 |
| non-full task를 earliest saturation deadline 순으로 비교하는가 | 일치 |
| 실행 중인 `Reserved`/`Running` task를 선점하지 않는가 | 일치 |
| `ITaskAllocator` 및 `StudentRobotSnapshot` 계약을 사용하는가 | 일치 |
| 거리 tie-breaker가 공식 `OperatingStations` 좌표만 사용하는가 | 일치 |
| `NextProductionAt() == -1` 시 생산 주기 fallback deadline을 적용하는가 | 일치 |
| `FleetManager`가 task와 conveyor reservation을 소유하는가 | 일치 |
| `MissionRequest` 전달과 `MissionResult` callback 상태 갱신을 구현하는가 | 일치 |
| B/D 구현이 없어도 environment/pending 검증이 가능한가 | 일치 |
| 색상 분류 또는 `RealProduct.isNormal`에 접근하는가 | 접근하지 않음 |
| Safety lock 또는 물리 제어를 수행하는가 | 수행하지 않음 |
| `[LOCKED] BaseAssets` 파일을 수정하는가 | 수정하지 않음 |

검토 결과, 현재 Fleet 구현에서 Common 스키마, 공식 API, 역할 문서, 과제 설명과 충돌하거나 책임 범위를 벗어난 동작은 확인되지 않았다.

## 8. 검증 내용

- Unity batch mode로 새 스크립트 import 및 프로젝트 컴파일을 수행했다.
- 생성된 `Assembly-CSharp.csproj`에 `EnvironmentScanner.cs`, `TaskAllocator.cs`, `FleetManager.cs`가 컴파일 대상으로 포함됨을 확인했다.
- `dotnet build "Assembly-CSharp.csproj" --no-restore` 결과 경고 0개, 오류 0개를 확인했다.
- `git diff --check`와 신규 파일 trailing whitespace 검색으로 변경 범위의 공백 오류가 없음을 확인했다.
- `git status --short -- "Assets/CPS_IC_PBL_2026/[LOCKED] BaseAssets"` 결과가 비어 있어 `[LOCKED]` 파일이 변경되지 않았음을 확인했다.
- Student `.cs`에서 `ProductClass` 또는 `productClass` 참조가 남지 않았음을 확인했다.
- Student 코드의 `RobotSnapshot` 참조는 `StudentRobotSnapshot.baseSnapshot`의 공식 `[LOCKED]` 타입 사용 한 건뿐이며, box pose 및 slot 인터페이스는 `BoxType` 기반임을 확인했다.

## 9. 후속 작업

현재 구현 이후 Slice A에서 이어질 작업은 다음과 같다.

1. Slice B/D가 준비되기 전에는 dummy robot agent를 사용해 `RunSchedulingCycle()`의 dispatch/callback 시나리오를 Play mode에서 검증한다.
2. B의 실제 `RobotAgent`가 준비되면 Bootstrap을 통해 `Configure(...)`에 연결한다.
3. D의 logger와 scene reference wiring이 준비되면 fallback 로그를 실제 telemetry 및 통합 scene 동작으로 교체한다.
