# Safety

## 담당 슬라이스

Slice D: Vision / Safety / Telemetry / Bootstrap

## 이 디렉터리의 책임

두 로봇이 같은 작업 공간에 동시에 접근하지 않도록 자원 lock, 중앙 구역 정책, deadlock 방지를 담당한다.

## 생성할 코드 파일

```text
ResourceLockManager.cs
PathPlanner.cs
DeadlockGuard.cs
```

## 파일별 작업

- `ResourceLockManager.cs`: `IResourceLockManager` 구현, `ResourceKey`별 mutex 관리
- `PathPlanner.cs`: `IPathPlanner` 구현, CentralZone lock 필요 여부 판단
- `DeadlockGuard.cs`: lock 대기 시간이 길어지는 상황 감지 및 timeout 정책 지원

## 관리할 ResourceKey

- `(Conveyor, 1~10)`: 컨베이어별 pick 작업 공간
- `(NormalBox, 100)`: Normal Box 앞 place 작업 공간
- `(AbnormalBox, 101)`: Abnormal Box 앞 place 작업 공간
- `(CentralZone, 0)`: RobotA/B 중앙 교차 구역
- `(RobotArmZone, id)`: 같은 station 또는 인접 station의 팔 작업 공간

## Lock 규칙

- 같은 `ResourceKey`는 동시에 한 로봇만 획득할 수 있다.
- 획득 성공 시 `ResourceLockToken`을 반환한다.
- mission 성공, 실패, timeout 모든 경로에서 lock이 해제되어야 한다.
- 같은 자원을 같은 robot/task가 중복 획득하려는 경우 정책을 명확히 처리한다.
- 오래 보유한 lock은 warning 로그를 남긴다.

## 권장 lock 순서

여러 lock이 필요한 경우 순서를 통일한다.

```text
CentralZone → Conveyor/Box → RobotArmZone
```

- CentralZone은 반대편 이동이나 중앙 교차가 필요할 때만 획득한다.
- Conveyor lock은 pick 작업 공간 점유용이다.
- NormalBox/AbnormalBox lock은 place 작업 공간 점유용이다.
- RobotArmZone lock은 arm의 approach/action/retract 동작 중 짧게 보유한다.

## PathPlanner 기준

- RobotA는 기본적으로 Conveyor 1~5 우선
- RobotB는 기본적으로 Conveyor 6~10 우선
- 반대편으로 이동해야 하면 CentralZone lock 필요
- box 접근은 NormalBox/AbnormalBox lock 필요
- 한쪽 queue가 비었고 반대쪽이 위험하면 교차 작업을 허용할 수 있다.

## DeadlockGuard 기준

- `WaitingForLock` 상태가 일정 시간 이상 지속되면 mission fail 또는 재계획하도록 지원한다.
- 두 로봇이 서로의 lock을 기다리는 상황을 telemetry로 남긴다.
- lock 실패 시 무한 대기하지 않도록 timeout 값을 둔다.

## 다른 슬라이스와의 연결

- Robot/MissionExecutor가 실제 미션 중 `TryAcquire()`와 `Release()`를 호출한다.
- Fleet의 reservation과 Safety lock은 별개다.
- Telemetry에 lock acquire, release, fail, timeout 로그를 남긴다.

## 완료 기준

- 같은 conveyor lock을 두 로봇이 동시에 획득할 수 없다.
- NormalBox/AbnormalBox lock이 각각 독립적으로 동작한다.
- CentralZone lock이 교차 이동을 제어한다.
- RobotArmZone lock이 팔 작업 공간 중복을 방지한다.
- lock 획득 실패가 무한 대기로 이어지지 않는다.
- 모든 mission 종료 경로에서 lock이 해제된다.

## 주의사항

- lock 정책은 충돌 회피용이지 점수 조작용이 아니다.
- lock을 오래 들고 있으면 처리 시간이 늘어나므로 필요한 구간에서만 보유한다.
- ResourceLockManager가 로봇 이동을 직접 수행하지 않는다. 이동은 Robot 담당이다.
