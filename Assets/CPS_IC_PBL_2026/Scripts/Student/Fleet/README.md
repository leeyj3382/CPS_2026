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

- `EnvironmentScanner.cs`: `IEnvironmentInfo.GetQueueLength(1~10)`, `NextProductionAt(1~10)`, 생산 주기 상수와 Fleet가 넘긴 reservation/마지막 배정 시각을 이용해 `ConveyorSnapshot[]` 생성
- `TaskAllocator.cs`: Fleet가 만든 미배정 `Pending` 작업 중 queue saturation deadline이 가장 이른 `WorkTask` 선택
- `FleetManager.cs`: scene에 붙이는 `MonoBehaviour`; 전체 loop 관리, task 생성/예약, RobotA/B idle 확인, `StartMission()` 호출, `MissionResult` callback 처리

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

1. `EnvironmentScanner.Scan()`으로 1~10번 컨베이어 snapshot을 모두 만든다. reservation과 마지막 배정 시각 상태는 `FleetManager`가 소유하고 scanner에 전달한다.
2. `FleetManager`가 관측 결과에 맞춰 미배정 `Pending` `WorkTask` 후보를 생성하거나 갱신한다.
3. `TaskAllocator`가 queue가 0이거나 reservation된 컨베이어의 task를 후보에서 제외한다.
4. RobotA/B 두 `IRobotAgent`의 상태를 확인한다.
5. idle 또는 `CanAcceptTask == true`인 로봇만 작업 후보로 둔다.
6. `TaskAllocator`로 saturation deadline이 가장 이른 pending task를 선택한다.
7. 선택한 conveyor를 Fleet 내부에서 reservation 처리하고 task 상태를 `Reserved`/`Running`으로 전환한다.
8. `MissionRequest`를 만들고 해당 RobotAgent의 `StartMission()`을 호출한다. 실행 중인 작업은 재선택하거나 중단하지 않는다.
9. `MissionResult` callback에서 reservation을 해제하고 task 상태를 갱신한다.
10. 실패하면 retry count 증가 또는 fail 처리 정책을 적용한다.

## 선택 정책: Non-Preemptive EDF-Inspired

- CPU scheduling의 EDF에서 착안하여, queue가 capacity에 도달할 것으로 예상되는 시각이 가장 이른 pending task를 먼저 선택한다.
- queue length가 capacity `3`에 이미 도달한 conveyor는 deadline이 즉시 도래한 것으로 보고 최우선으로 선택한다.
- `nextProductionAt`이 유효하면 `nextProductionAt + (남은 칸 수 - 1) * productionPeriod`를 saturation deadline으로 사용한다.
- 현재처럼 `nextProductionAt == -1`이면 `(남은 칸 수 * productionPeriod)`를 상대 deadline fallback으로 사용한다.
- 이미 reservation된 conveyor는 제외
- `Reserved` 또는 `Running` task는 후보가 아니며, 시작된 mission은 완료 callback까지 수행하는 non-preemptive 정책이다.
- deadline이 같으면 짧은 생산 주기, 공식 station 기준 가까운 거리, 오래 배정되지 않은 conveyor, 오래된 task, 낮은 conveyor id 순서로 결정한다.
- `priorityScore`에는 deadline이 이를수록 큰 값이 되도록 `-estimatedSaturationDeadline`을 기록한다.

이 정책은 EDF에서 착안한 overflow 방지 heuristic이다. 로봇이 2대이고 mission 수행 시간이 별도 모델링되지 않았으므로 CPU EDF의 최적성 보장을 주장하지 않는다.

`TaskAllocator`에 선택 가능한 pending task가 없으면 `null`을 반환한다. task 생성, reservation 설정, idle robot 확인과 실제 배정은 `FleetManager`가 담당한다.

## FleetManager 연결 방식

- `FleetManager`만 scene의 Student GameObject에 component로 붙인다.
- Inspector에서 공식 `EnvironmentInfo`와 필요 시 `OperatingStations`를 연결한다.
- `EnvironmentScanner`와 `TaskAllocator`는 `FleetManager`가 내부에서 생성하므로 GameObject에 직접 붙이지 않는다.
- Slice B/D가 준비되면 `StudentBootstrap`이 `Configure(...)`로 `IRobotAgent` A/B와 `ITelemetryLogger`를 주입한다.
- B/D가 아직 없으면 environment polling과 pending task 생성까지 실행할 수 있고, agent가 없으므로 mission dispatch는 수행하지 않는다.
- 거리 동점 비교는 `enableDistanceTieBreaker`가 켜지고 `UpdateRobotSnapshot(...)`으로 실제 robot 위치를 공급하는 통합 단계에서 사용한다.

## 다른 슬라이스와의 연결

- Common의 `WorkTask`, `MissionRequest`, `MissionResult`, `ConveyorSnapshot`, `StudentRobotSnapshot` 사용
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
- 실행 중인 task는 더 빠른 deadline의 새 task가 생겨도 선점되지 않는다.

## 주의사항

- 팔 좌표 계산, grip, 색상 판정, box slot 계산은 이 디렉터리에서 직접 처리하지 않는다.
- `IRobotAgent` interface를 통해서만 Robot 실행 계층을 호출한다.
- Fleet reservation과 Safety lock은 목적이 다르다. reservation은 scheduling 중복 방지, lock은 실제 작업 공간 충돌 방지다.
