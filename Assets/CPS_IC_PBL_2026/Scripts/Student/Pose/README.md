# Pose

## 담당 슬라이스

Slice C: Pose / Calibration / Palletizing

## 이 디렉터리의 책임

로봇 팔이 어디로 움직여야 하는지에 대한 world position 좌표를 담당한다. 컨베이어별 pick pose와 Normal/Abnormal box 내부 place slot을 제공한다.

## 생성할 코드 파일

```text
PoseTable.cs
CalibrationManager.cs
Palletizer.cs
```

## 파일별 작업

- `PoseTable.cs`: `IPoseProvider` 구현, conveyor `1~10` pick pose와 box base pose 제공
- `CalibrationManager.cs`: Unity 테스트 중 approach/action/retract 좌표를 빠르게 확인하고 보정하는 보조 도구
- `Palletizer.cs`: `IPalletizer` 구현, product class별 slot 예약/확정/해제 관리

## 입력

- Robot에서 전달하는 conveyor id
- Robot에서 전달하는 `ProductClass`
- Robot에서 전달하는 robot id, task id
- 가능하면 Bootstrap에서 연결된 `IEnvironmentInfo.GetBoxOccupancy(BoxType box)`

## 출력

- `StationPose`: `approachPos`, `actionPos`, `retractPos`
- `BoxSlotPose`: `approachPos`, `placePos`, `retractPos`, `slotIndex`
- slot reservation/commit/release 상태

## PoseTable 구현 기준

- conveyor `1~10` 각각에 대해 pick용 `StationPose`를 제공한다.
- `actionPos`는 실제 pick 위치다.
- `approachPos`는 물품 위쪽 접근 위치다.
- `retractPos`는 grip 후 안전하게 들어 올리는 위치다.
- box base pose는 Normal `100`, Abnormal `101` 기준으로 제공한다.
- 좌표는 Robot의 `MoveArmTo()`에 바로 넘길 수 있는 world position이어야 한다.

## Palletizer 구현 기준

- Normal Box와 Abnormal Box의 slot index를 별도로 관리한다.
- `ReserveNextSlot(productClass, robotId, taskId)`는 같은 slot 중복 사용을 막아야 한다.
- place 성공 시 `CommitSlot(taskId)`로 확정한다.
- place 실패 또는 mission 실패 시 `ReleaseSlot(taskId)`로 예약을 되돌린다.
- `GetBoxOccupancy()`와 자체 slot index를 비교해 크게 어긋나면 로그로 남긴다.
- occupancy 검증은 slot 좌표 계산을 대체하지 않고, 누락/이탈/commit 오류를 찾는 보조 검증으로 사용한다.

## 구현 순서

1. conveyor 1의 임시 pick pose를 잡는다.
2. RobotA 단일 grip 성공을 확인한다.
3. conveyor 2~10 pose를 확장한다.
4. Normal Box place pose를 잡는다.
5. Abnormal Box place pose를 잡는다.
6. 1층 grid slot 배치를 구현한다.
7. `ReserveNextSlot()`, `CommitSlot()`, `ReleaseSlot()` 동작을 검증한다.
8. `GetBoxOccupancy()`와 자체 slot index 비교 로그를 추가한다.

## 완료 기준

- conveyor `1~10`에 대해 `GetConveyorPickPose()`가 유효한 pose를 반환한다.
- 각 pose에서 grip 후보가 들어오고 `IsGraspReady`가 될 수 있다.
- Normal/Abnormal slot index가 순서대로 증가한다.
- 두 로봇이 동시에 같은 slot을 예약할 수 없다.
- place 실패 시 slot 예약을 되돌릴 수 있다.
- 물품이 박스 밖으로 나가지 않는다.
- 자유낙하가 발생하지 않도록 낮은 place pose를 제공한다.

## 주의사항

- `StationPose` 필드명은 `actionPos`를 사용한다.
- `RealProduct.isNormal` 같은 정답성 정보는 사용하지 않는다.
- 좌표 보정은 실제 Unity 실행 테스트를 기준으로 한다.
- 박스 벽이나 물품을 통과하는 좌표는 피한다.
