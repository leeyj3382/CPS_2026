# Fleet

## 담당 슬라이스

Slice A: Fleet / Scheduling / Common Schema

## 이 디렉터리의 책임

10개 컨베이어 queue 상태를 읽고, 처리할 작업을 만들고, RobotA/B 중 가능한 로봇에게 `MissionRequest`를 배정한다. 전체 시스템의 scheduling 계층이며, 물리 동작은 직접 수행하지 않는다.

## 생성할 코드 파일

```text
FleetManager.cs
EnvironmentScanner.cs
TaskAllocator.cs
```

## 파일별 작업

- `EnvironmentScanner.cs`: `IEnvironmentInfo.GetQueueLength(1~10)`, `NextProductionAt(1~10)`, 생산 주기 상수를 이용해 `ConveyorSnapshot[]` 생성
- `TaskAllocator.cs`: queue length, 생산 주기, overflow 위험, 대기 시간, 거리, reservation 여부를 이용해 처리할 `WorkTask` 선택
- `FleetManager.cs`: 전체 loop 관리, task 생성/예약, RobotA/B idle 확인, `StartMission()` 호출, `MissionResult` callback 처리

## 입력

- `IEnvironmentInfo`: queue length, current time, next production time
- RobotA/B의 `IRobotAgent`: `CanAcceptTask`, `State`, `RobotId`, `StartMission()`
- `ITelemetryLogger`: task 생성/할당/결과 로그
- Fleet 내부 reservation 상태: 같은 conveyor가 중복 배정되지 않도록 관리

## 출력

- `WorkTask`: 처리할 컨베이어 작업
- `MissionRequest`: RobotAgent에게 넘기는 실행 요청
- task 상태 갱신: pending, reserved, running, completed, failed
- telemetry 로그: task created, assigned, mission result

## 구현 흐름

1. `EnvironmentScanner`로 1~10번 컨베이어 snapshot을 만든다.
2. queue가 0인 컨베이어는 작업 후보에서 제외한다.
3. RobotA/B 두 `IRobotAgent`의 상태를 확인한다.
4. idle 또는 `CanAcceptTask == true`인 로봇만 작업 후보로 둔다.
5. `TaskAllocator`로 가장 급한 컨베이어를 선택한다.
6. 선택한 conveyor를 Fleet 내부에서 reservation 처리한다.
7. `MissionRequest`를 만들고 해당 RobotAgent의 `StartMission()`을 호출한다.
8. `MissionResult` callback에서 reservation을 해제하고 task 상태를 갱신한다.
9. 실패하면 retry count 증가 또는 fail 처리 정책을 적용한다.

## 우선순위 기준

- queue length 3인 컨베이어는 최우선 후보
- queue length 2이고 생산 주기가 짧은 컨베이어는 높은 우선순위
- 1~4번 빠른 컨베이어는 overflow 위험을 높게 반영
- 이미 reservation된 conveyor는 제외
- 로봇과 너무 먼 conveyor는 distance cost로 감점
- 오래 처리되지 않은 conveyor는 waiting score로 보정

## 다른 슬라이스와의 연결

- Common의 `WorkTask`, `MissionRequest`, `MissionResult`, `ConveyorSnapshot`, `RobotSnapshot` 사용
- Robot의 `IRobotAgent.StartMission()`만 호출
- Telemetry의 `ITelemetryLogger`로 task 로그 기록
- Safety의 물리 lock은 Fleet가 직접 잡지 않는다. Fleet는 conveyor reservation만 관리한다.

## 완료 기준

- `GetQueueLength(1~10)` 결과로 `ConveyorSnapshot[]`을 만들 수 있다.
- queue가 0인 컨베이어에는 작업을 만들지 않는다.
- RobotA/B 중 idle 로봇에게만 작업을 배정한다.
- 같은 conveyor가 두 로봇에게 중복 배정되지 않는다.
- `MissionResult` 수신 후 reservation이 해제된다.
- 실패 시 retry 또는 fail 상태가 기록된다.

## 주의사항

- 팔 좌표 계산, grip, 색상 판정, box slot 계산은 이 디렉터리에서 직접 처리하지 않는다.
- `IRobotAgent` interface를 통해서만 Robot 실행 계층을 호출한다.
- Fleet reservation과 Safety lock은 목적이 다르다. reservation은 scheduling 중복 방지, lock은 실제 작업 공간 충돌 방지다.
