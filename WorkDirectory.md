# CPS 2026 Work Directory Guide

이 문서는 각 슬라이스 담당자가 어떤 디렉터리에서 어떤 이름의 코드 파일을 만들어 작업해야 하는지 정리한 가이드다.

모든 학생 코드는 아래 경로 하위에 작성한다.

```text
Assets/CPS_IC_PBL_2026/Scripts/Student/
```

`Assets/CPS_IC_PBL_2026/[LOCKED] BaseAssets/` 하위 파일은 직접 수정하지 않는다.

---

## Slice A. Fleet / Scheduling / Common Schema

담당자는 공통 스키마와 전체 작업 할당 흐름을 담당한다.

### 작업 디렉터리

```text
Assets/CPS_IC_PBL_2026/Scripts/Student/Common/
Assets/CPS_IC_PBL_2026/Scripts/Student/Fleet/
```

### 생성할 코드 파일

```text
Assets/CPS_IC_PBL_2026/Scripts/Student/Common/StudentEnums.cs
Assets/CPS_IC_PBL_2026/Scripts/Student/Common/StudentSchemas.cs
Assets/CPS_IC_PBL_2026/Scripts/Student/Common/StudentInterfaces.cs
Assets/CPS_IC_PBL_2026/Scripts/Student/Common/StudentConstants.cs

Assets/CPS_IC_PBL_2026/Scripts/Student/Fleet/FleetManager.cs
Assets/CPS_IC_PBL_2026/Scripts/Student/Fleet/EnvironmentScanner.cs
Assets/CPS_IC_PBL_2026/Scripts/Student/Fleet/TaskAllocator.cs
```

### 핵심 작업

- 공통 enum, schema, interface, constant 정의
- 1~10번 컨베이어 queue 상태 스캔
- 작업 우선순위 계산
- RobotA/B 중 idle 로봇에게 `MissionRequest` 전달
- `MissionResult` callback 처리

---

## Slice B. Robot Mission / Motion / Gripper

담당자는 로봇 1대가 물품 1개를 처리하는 실행 흐름을 담당한다. 단, 이 구현은 RobotA/B 두 인스턴스에서 모두 재사용 가능해야 한다.

### 작업 디렉터리

```text
Assets/CPS_IC_PBL_2026/Scripts/Student/Robot/
```

### 생성할 코드 파일

```text
Assets/CPS_IC_PBL_2026/Scripts/Student/Robot/RobotAgent.cs
Assets/CPS_IC_PBL_2026/Scripts/Student/Robot/MissionExecutor.cs
Assets/CPS_IC_PBL_2026/Scripts/Student/Robot/GripperAdapter.cs
```

### 핵심 작업

- `RobotAgent` 상태 머신 구현
- `MissionRequest` 수신 후 미션 실행
- 컨베이어 이동, pick, grip, inspect, box 이동, place, release 수행
- `GripperAdapter`로 `SuctionGripper` 호출 감싸기
- RobotA/B 각각에 같은 실행 흐름을 적용할 수 있도록 instance별 참조를 분리
- 성공/실패 결과를 `MissionResult`로 반환

---

## Slice C. Pose / Calibration / Palletizing

담당자는 pick/place 좌표와 box slot 관리를 담당한다.

### 작업 디렉터리

```text
Assets/CPS_IC_PBL_2026/Scripts/Student/Pose/
```

### 생성할 코드 파일

```text
Assets/CPS_IC_PBL_2026/Scripts/Student/Pose/PoseTable.cs
Assets/CPS_IC_PBL_2026/Scripts/Student/Pose/CalibrationManager.cs
Assets/CPS_IC_PBL_2026/Scripts/Student/Pose/Palletizer.cs
```

### 핵심 작업

- 컨베이어 1~10 pick pose 제공
- Normal Box, Abnormal Box base pose 제공
- `ReserveNextSlot(BoxType boxType, int robotId, int taskId)`, `CommitSlot()`, `ReleaseSlot()` 구현
- `GetBoxOccupancy()`와 자체 slot index 비교로 적재 상태 검증
- place 실패 시 slot 예약 되돌리기
- 박스 안에 안정적으로 놓이도록 slot 좌표 보정

---

## Slice D. Vision / Safety / Telemetry / Bootstrap

담당자는 색상 분류, lock, deadlock 방지, 로그, 씬 참조 연결을 담당한다.

### 작업 디렉터리

```text
Assets/CPS_IC_PBL_2026/Scripts/Student/Bootstrap/
Assets/CPS_IC_PBL_2026/Scripts/Student/Vision/
Assets/CPS_IC_PBL_2026/Scripts/Student/Safety/
Assets/CPS_IC_PBL_2026/Scripts/Student/Telemetry/
```

### 생성할 코드 파일

```text
Assets/CPS_IC_PBL_2026/Scripts/Student/Bootstrap/StudentBootstrap.cs
Assets/CPS_IC_PBL_2026/Scripts/Student/Bootstrap/StudentSceneReferences.cs

Assets/CPS_IC_PBL_2026/Scripts/Student/Vision/ColorClassifier.cs

Assets/CPS_IC_PBL_2026/Scripts/Student/Safety/ResourceLockManager.cs
Assets/CPS_IC_PBL_2026/Scripts/Student/Safety/PathPlanner.cs
Assets/CPS_IC_PBL_2026/Scripts/Student/Safety/DeadlockGuard.cs

Assets/CPS_IC_PBL_2026/Scripts/Student/Telemetry/TelemetryLogger.cs
```

### 핵심 작업

- `ColorSensor.area.color` 또는 `ColorArea.color` 기반 Normal/Abnormal 분류
- Conveyor, NormalBox, AbnormalBox, CentralZone, RobotArmZone lock 관리
- 중앙 구역 이동 정책과 deadlock timeout 처리
- task, lock, grip, classification, mission result 로그 기록
- `StudentSceneReferences`로 Unity Inspector reference 관리

---

## 전체 디렉터리와 파일 배치 요약

```text
Assets/CPS_IC_PBL_2026/Scripts/Student/
├── Common/
│   ├── StudentEnums.cs
│   ├── StudentSchemas.cs
│   ├── StudentInterfaces.cs
│   └── StudentConstants.cs
├── Bootstrap/
│   ├── StudentBootstrap.cs
│   └── StudentSceneReferences.cs
├── Fleet/
│   ├── FleetManager.cs
│   ├── EnvironmentScanner.cs
│   └── TaskAllocator.cs
├── Robot/
│   ├── RobotAgent.cs
│   ├── MissionExecutor.cs
│   └── GripperAdapter.cs
├── Pose/
│   ├── PoseTable.cs
│   ├── CalibrationManager.cs
│   └── Palletizer.cs
├── Vision/
│   └── ColorClassifier.cs
├── Safety/
│   ├── ResourceLockManager.cs
│   ├── PathPlanner.cs
│   └── DeadlockGuard.cs
└── Telemetry/
    └── TelemetryLogger.cs
```

## 작업 원칙

- 각 담당자는 위에 지정된 디렉터리와 파일명으로 작업한다.
- `Common` 파일은 모든 슬라이스가 의존하므로 먼저 합의한 뒤 변경한다.
- Unity scene reference 연결은 `Bootstrap/StudentSceneReferences.cs`를 기준으로 관리한다.
- `[LOCKED] BaseAssets` 원본 스크립트와 prefab은 수정하지 않는다.
- 다른 슬라이스의 구현 내부를 직접 호출하지 말고 `StudentInterfaces.cs`의 interface를 통해 연결한다.
