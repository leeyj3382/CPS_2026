# Robot

## 담당 슬라이스

Slice B: Robot Mission / Motion / Gripper

## 이 디렉터리의 책임

`MissionRequest` 하나를 받아 로봇 1대가 물품 하나를 실제로 처리하는 실행 흐름을 담당한다. 이동, pick, grip, inspect, place, release까지 수행하고 `MissionResult`를 Fleet로 반환한다.

이 구현은 RobotA 전용이 아니다. RobotA/B 두 인스턴스에서 같은 `RobotAgent` / `MissionExecutor` 코드를 재사용해야 한다. 어떤 로봇이 어떤 작업을 받을지는 Fleet가 결정하고, RobotA/B별 controller, gripper, colorArea 참조는 Bootstrap에서 연결한다.

## 생성할 코드 파일

```text
RobotAgent.cs
MissionExecutor.cs
GripperAdapter.cs
```

## 파일별 작업

- `RobotAgent.cs`: `IRobotAgent` 구현, robot id/state/can accept task 관리, `StartMission()` 진입점 제공
- `MissionExecutor.cs`: 컨베이어 이동, pick, grip, 색상 검사, box 이동, palletizer slot place, 실패 처리 실행
- `GripperAdapter.cs`: `SuctionGripper`의 `IsGraspReady`, `CurrentCandidate`, `TryGrip()`, `IsHolding`, `Release()` 호출을 안정적으로 감쌈

## 입력

- Fleet에서 전달하는 `MissionRequest`
- Bootstrap에서 연결한 robot별 `IRobotController`
- robot별 `SuctionGripper`
- robot별 `ColorArea`
- Pose의 `IPoseProvider`, `IPalletizer`
- Vision의 `IColorClassifier`
- Safety의 `IResourceLockManager`, `IPathPlanner`
- Telemetry의 `ITelemetryLogger`

## 출력

- Fleet callback으로 전달하는 `MissionResult`
- Robot state 변화
- grip, lock, classification, failure telemetry

## 기본 미션 흐름

1. `StartMission(request, onFinished)`에서 busy/state를 갱신한다.
2. `IPathPlanner`로 CentralZone lock 필요 여부를 확인한다.
3. 필요한 lock을 정해진 순서로 획득한다.
4. `IRobotController.GoToOperatingStation(conveyorId)`로 이동한다.
5. `IPoseProvider.GetConveyorPickPose(conveyorId)`에서 pick pose를 받는다.
6. `approachPos → actionPos → retractPos` 순서로 arm을 움직인다.
7. `GripperAdapter`로 grasp ready를 기다리고 `TryGrip()`을 수행한다.
8. `IsHolding`으로 grip 성공 여부를 확인한다.
9. `ColorArea.color`를 읽어 `IColorClassifier.Classify()`에 전달한다.
10. Normal이면 station `100`, Abnormal이면 station `101`로 목적지를 정한다.
11. box lock을 얻고 `GoToOperatingStation(100 또는 101)`로 이동한다.
12. `IPalletizer.ReserveNextSlot()`으로 place slot을 예약한다.
13. slot의 `approachPos → placePos → retractPos` 순서로 arm을 움직인다.
14. `Release()` 후 place 성공이면 `CommitSlot(taskId)`를 호출한다.
15. 모든 lock을 해제하고 성공 `MissionResult`를 반환한다.

## 실패 처리 규칙

- grip 실패: retry 후 실패하면 `MissionFailureReason.GripFailed`
- 색상 신뢰도 낮음: 재검사 후 실패하면 `ClassificationFailed`
- lock 획득 실패 또는 timeout: `BoxLockFailed`, `CollisionRisk`, `MoveTimeout` 등 적절한 reason 기록
- place 실패: 예약한 slot이 있으면 `ReleaseSlot(taskId)` 호출
- 어떤 실패 경로에서도 획득한 lock은 반드시 해제
- 들고 있는 물품이 있으면 안전한 release 또는 실패 처리 정책을 명확히 둔다.

## Lock 사용 기준

- 여러 lock이 필요한 경우 Safety 문서의 순서를 따른다.
- 권장 순서: `CentralZone → Conveyor/Box → RobotArmZone`
- `RobotArmZone`은 arm의 approach/action/retract 동작 중 짧게 보유한다.
- Conveyor lock과 box lock을 동시에 오래 들고 있지 않는다.

## 완료 기준

- RobotA가 `MissionRequest(conveyorId=1)`로 물품 1개를 처리할 수 있다.
- RobotB도 같은 코드 경로로 물품 1개를 처리할 수 있다.
- 같은 `RobotAgent` / `MissionExecutor` 구현을 RobotA/B 두 인스턴스에 재사용할 수 있다.
- `TryGrip()` 후 `IsHolding`을 확인한다.
- 색상 분류 결과에 따라 station `100` 또는 `101`로 이동한다.
- `MissionResult`가 success/failure reason을 포함해 Fleet로 반환된다.
- 실패/timeout 경로에서 lock과 slot reservation이 정리된다.

## 주의사항

- 작업 우선순위나 어떤 컨베이어를 고를지는 Fleet 담당이다.
- pick/place 좌표 계산은 Pose 담당이다.
- 색상 판정 로직은 Vision 담당이다.
- lock 정책 구현은 Safety 담당이다.
- `[LOCKED] BaseAssets`의 RobotController, SuctionGripper 원본은 수정하지 않는다.
