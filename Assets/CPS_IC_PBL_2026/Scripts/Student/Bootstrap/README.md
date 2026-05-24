# Bootstrap

## 담당 슬라이스

Slice D: Vision / Safety / Telemetry / Bootstrap

## 이 디렉터리의 책임

씬에 배치된 RobotA, RobotB, EnvironmentInfo, Gripper, ColorSensor/ColorArea, BoxTrigger 등을 학생 코드와 연결하고, 전체 학생 시스템이 올바른 순서로 초기화되도록 한다.

Bootstrap은 로직을 많이 넣는 곳이 아니다. 핵심은 scene reference를 한 곳에 모으고, 각 슬라이스 구현체가 서로 필요한 참조를 받을 수 있게 연결하는 것이다.

## 생성할 코드 파일

```text
StudentBootstrap.cs
StudentSceneReferences.cs
```

## 파일별 작업

- `StudentSceneReferences.cs`: Unity Inspector에서 연결할 scene object reference를 모아둔다.
- `StudentBootstrap.cs`: FleetManager, RobotAgent A/B, PoseTable, Palletizer, ColorClassifier, ResourceLockManager, TelemetryLogger 등을 초기화/연결한다.

## StudentSceneReferences에 필요한 참조

- RobotA `RobotController`
- RobotB `RobotController`
- `IEnvironmentInfo` 구현 컴포넌트
- RobotA `SuctionGripper`
- RobotB `SuctionGripper`
- RobotA `ColorSensor` 또는 `ColorArea`
- RobotB `ColorSensor` 또는 `ColorArea`
- Normal Box `BoxTrigger`
- Abnormal Box `BoxTrigger`
- 필요 시 FleetManager, PoseTable, Palletizer, ColorClassifier, ResourceLockManager, TelemetryLogger

## StudentBootstrap 연결 기준

- RobotA용 `RobotAgent`는 RobotA controller, RobotA gripper, RobotA color area를 받아야 한다.
- RobotB용 `RobotAgent`는 RobotB controller, RobotB gripper, RobotB color area를 받아야 한다.
- `ColorSensor`를 연결하는 경우 실제 색상값은 `ColorSensor.area.color`에서 읽는다.
- FleetManager는 RobotA/B 두 `IRobotAgent` 인스턴스를 모두 받아야 한다.
- RobotAgent/MissionExecutor는 Pose, Vision, Safety, Telemetry 구현체를 interface로 받아야 한다.
- 다른 슬라이스가 scene을 직접 뒤지기보다 `StudentSceneReferences`를 통해 필요한 참조를 받게 한다.

## 초기화 순서

1. `StudentSceneReferences`에 Inspector reference를 연결한다.
2. Common schema compile을 먼저 확인한다.
3. Vision/Safety/Telemetry/Pose 구현체를 준비한다.
4. RobotA/B용 RobotAgent를 각각 준비한다.
5. FleetManager에 environment, RobotA/B agent, logger를 연결한다.
6. Play 실행 시 missing reference가 없는지 확인한다.

## 완료 기준

- Inspector에서 RobotA/B, EnvironmentInfo, Gripper, ColorSensor/ColorArea, BoxTrigger를 연결할 수 있다.
- RobotA/B agent가 서로 다른 controller, gripper, color area를 가진다.
- FleetManager가 RobotA/B 두 agent를 모두 볼 수 있다.
- `StudentSceneReferences.EnvironmentInfo`가 null이 아니다.
- scene reference 연결을 한 명이 관리해 scene merge conflict를 줄일 수 있다.

## 주의사항

- Unity scene 파일 충돌을 줄이기 위해 Inspector reference 연결은 팀원 4 또는 팀장 1명이 관리한다.
- `FindObjectOfType()` 남발은 피하고, 필요한 참조는 `StudentSceneReferences`를 통해 받는다.
- `[LOCKED] BaseAssets` 원본 prefab이나 script는 수정하지 않는다.
- Bootstrap에 scheduling, motion, pose, classification, lock 로직을 섞지 않는다.
