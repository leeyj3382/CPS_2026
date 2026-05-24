# Common

## 담당 슬라이스

Slice A: Fleet / Scheduling / Common Schema

## 이 디렉터리의 책임

모든 슬라이스가 공유하는 enum, 데이터 모델, interface, constant를 정의한다. 다른 슬라이스는 각자 임의 타입을 새로 만들기보다 이 디렉터리의 타입을 기준으로 연결해야 한다.

`Student/Common`은 `[LOCKED] BaseAssets/Common`을 대체하지 않는다. `[LOCKED] Common`은 베이스 시스템의 공식 API/타입이고, `Student/Common`은 학생 코드 내부 모듈 간 계약 계층이다. 색상 분류 결과는 `[LOCKED]`의 `CPS.ICPBL.Common.ClassificationResult`, 목적 박스와 Palletizer 입력은 `CPS.ICPBL.Common.BoxType`을 재사용한다. 학생용 `ProductClass`는 더 이상 사용하지 않는다.

Common은 가장 먼저 합의되어야 한다. 여기의 schema가 바뀌면 Fleet, Robot, Pose, Vision, Safety, Telemetry가 모두 영향을 받는다.

## 생성할 코드 파일

```text
StudentEnums.cs
StudentSchemas.cs
StudentInterfaces.cs
StudentConstants.cs
```

## 파일별 작업

- `StudentEnums.cs`: `RobotRuntimeState`, `TaskStatus`, `LockResourceType`, `MissionFailureReason` 정의
- `StudentSchemas.cs`: `ConveyorSnapshot`, `StudentRobotSnapshot`, `WorkTask`, `MissionRequest`, `MissionResult`, `StationPose`, `BoxSlotPose`, `ColorClassificationResult`, `ResourceKey`, `ResourceLockToken` 정의
- `StudentInterfaces.cs`: `IRobotAgent`, `ITaskAllocator`, `IPoseProvider`, `IPalletizer`, `IColorClassifier`, `IResourceLockManager`, `IPathPlanner`, `ITelemetryLogger` 정의
- `StudentConstants.cs`: station id, robot id, conveyor id 범위, box station id, conveyor 생산 주기, timeout 기본값, 색상 기준값 같은 공통 상수 정의

## 반드시 맞출 스키마

- Station ID: Conveyor `1~10`, Normal Box `100`, Abnormal Box `101`
- Robot ID: RobotA `0`, RobotB `1`
- `StationPose`: `approachPos`, `actionPos`, `retractPos` 사용
- `BoxSlotPose`: `approachPos`, `placePos`, `retractPos` 사용
- `ResourceKey`: dictionary key로 쓰이므로 `struct` 형태와 equality/hash 구현 유지
- `LockResourceType`: `Conveyor`, `NormalBox`, `AbnormalBox`, `CentralZone`, `RobotArmZone`
  - `[LOCKED] ResourceType` 대체가 아니라 `CentralZone` / `RobotArmZone`까지 포함하는 학생용 확장 lock 타입
- `MissionResult`: 성공/실패, 분류 결과, 목적 station, 실패 사유를 Fleet로 돌려줄 수 있어야 함
- `StudentRobotSnapshot`: `[LOCKED] Common.RobotSnapshot`을 `baseSnapshot`으로 재사용하고 학생 runtime state만 확장
- `MissionResult.classificationResult`, `ColorClassificationResult.result`: `ClassificationResult` 사용
- `BoxSlotPose.boxType`, `GetBoxBasePose()`, `ReserveNextSlot()`: `BoxType` 사용

## 외부로 제공하는 것

- Slice A는 `WorkTask`, `MissionRequest`, `MissionResult`, `IRobotAgent`, `ITaskAllocator`를 사용한다.
- Slice B는 `MissionRequest`, `MissionResult`, `RobotRuntimeState`, `IPoseProvider`, `IPalletizer`, `IColorClassifier`, `IResourceLockManager`, `IPathPlanner`를 사용한다.
- Slice C는 `StationPose`, `BoxSlotPose`, `IPoseProvider`, `IPalletizer`를 구현한다.
- Slice D는 `ColorClassificationResult`, `ResourceKey`, `ResourceLockToken`, `IColorClassifier`, `IResourceLockManager`, `IPathPlanner`, `ITelemetryLogger`를 구현한다.

## 구현 순서

1. enum을 먼저 작성한다.
2. schema class/struct를 작성한다.
3. interface를 작성한다.
4. 공통 상수를 작성한다.
5. Unity compile error가 없는지 확인한다.
6. 다른 슬라이스가 이 타입만 import해서 skeleton을 만들 수 있는지 확인한다.

## 완료 기준

- 모든 공통 타입이 `CPS.ICPBL.Student` namespace 아래에서 컴파일된다.
- `ResourceKey`를 lock dictionary key로 안전하게 사용할 수 있다.
- `StationPose` 필드명이 문서 전체와 동일하게 `actionPos`로 통일되어 있다.
- 각 슬라이스가 필요한 interface를 이 디렉터리에서 참조할 수 있다.

## 주의사항

- schema나 interface 변경은 팀 전체에 공유한 뒤 반영한다.
- 구현 편의를 위해 Slice별 중복 enum이나 중복 DTO를 만들지 않는다.
- `[LOCKED] BaseAssets` 타입을 직접 수정하지 않고, 필요한 경우 adapter/interface로 감싼다.
