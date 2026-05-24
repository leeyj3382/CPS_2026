# CPS 2026 IC-PBL 과업 정의서 v2

> 기준 자료  
> - 과제 설명 Notion: https://smooth-kidney-f73.notion.site/2026-CPS-IC-PBL-8cf70ecf442e82eeb3f801fbf200be5c  
> - GitHub 레포: https://github.com/leeyj3382/CPS_2026  
> - 강의 PDF: `사이버물리시스템_IC-PBL_Introduction_2026(2).pdf`  
> - 전제: Unity 씬 구성, 로봇/컨베이어/박스/채점기 등 기본 오브젝트 배치는 완료되어 있다고 가정한다.
>
> 이 문서는 **역할 분담용 과업 정의서**다.  
> 따라서 “무엇을 개발해야 하는가”, “누가 무엇을 맡아야 하는가”, “어떤 항목을 테스트해야 하는가”를 중심으로 정리한다.

---

## 1. 한 줄 결론

이번 과제의 핵심은 **제공된 Unity 디지털 트윈 환경에서 2대의 모바일 매니퓰레이터를 제어하는 학생용 스크립트를 작성하여, 10개 컨베이어 큐의 물품을 빠르게 픽업하고, 색상 센서 기반 검사로 Normal/Abnormal을 분류한 뒤, 각 박스에 물리적으로 안정적으로 팔레타이징하는 것**이다.

즉, 새로 만들어야 할 것은 로봇 모델이나 씬이 아니라 다음 제어 로직이다.

- 어떤 컨베이어를 먼저 처리할지 결정하는 **작업 우선순위 정책**
- 두 로봇 중 누가 어떤 작업을 처리할지 정하는 **작업 할당 정책**
- 두 로봇이 같은 위치/자원을 동시에 쓰지 않게 하는 **충돌 회피 및 Lock 정책**
- 로봇이 이동, 접근, Pick, 검사, 이동, Place, Release를 수행하는 **미션 실행 로직**
- 물품을 실제로 안정적으로 집기 위한 **Pick/Place Pose 캘리브레이션**
- ColorSensor/ColorArea 결과를 이용한 **Normal/Abnormal 분류 로직**
- Normal Box / Abnormal Box에 가지런히 넣는 **팔레타이징 정책**
- 시간, 미적재, 분류 오류, 충돌을 줄이는 **성능 최적화 및 디버깅 로직**

---

## 2. 과제에서 해야 하는 것 / 하지 않아도 되는 것

### 2.1 해야 하는 것

| 구분 | 해야 할 일 |
|---|---|
| 로봇 제어 | RobotA, RobotB 2대를 학생 코드에서 제어 |
| 큐 감시 | 10개 컨베이어의 queue length를 지속적으로 확인 |
| 작업 선택 | 현재 큐 상태와 생산 주기를 보고 처리할 컨베이어 선택 |
| 작업 할당 | RobotA/B 중 적절한 로봇에게 작업 배정 |
| 이동 | 컨베이어 운영 위치와 박스 운영 위치로 이동 |
| Pick | UR5e 팔과 EPick/SuctionGripper로 물품 1개를 집음 |
| 색상 검사 | ColorSensor/ColorArea로 물품 색상을 감지 |
| 분류 | 파란색 계열은 Normal, 빨간색 계열은 Abnormal로 판정 |
| Place | Normal Box 또는 Abnormal Box에 낮은 높이에서 안정적으로 내려놓음 |
| 팔레타이징 | 박스 내부 slot 좌표를 관리해 가지런히 배치 |
| 충돌 회피 | 로봇끼리 같은 station, box, 이동 구역을 동시에 점유하지 않게 함 |
| 성능 개선 | 전체 완료 시간, 미적재, 분류 오류, 충돌 횟수 최소화 |
| 제출 | 발표자료, 영상, 보고서, 빌드, 프로젝트 zip 준비 |

### 2.2 하지 않아도 되는 것

| 구분 | 이유 |
|---|---|
| Unity 씬 전체 제작 | 기본 씬 구성은 제공된 것으로 가정 |
| 컨베이어 생산 시스템 구현 | 생산/큐/폐기 동작은 베이스 환경에서 제공 |
| UR5e IK 수식 구현 | `MoveArmTo()` 호출 시 베이스 IK가 처리 |
| 로봇 모델링/프리팹 제작 | 모바일 매니퓰레이터 prefab 제공 |
| 채점기 구현 | 채점기/스코어 UI는 제공 |
| `RealProduct.isNormal` 직접 읽기 | 금지. 색상 센서 기반 검사만 허용 |

---

## 3. 과제 환경 요약

### 3.1 생산 환경

| 항목 | 내용 |
|---|---|
| 컨베이어 수 | 10개 |
| 총 생산 물품 수 | 64개 |
| 생산 시간 | 180초 |
| 0초 생산 여부 | 0초에는 생산하지 않음 |
| 큐 용량 | 컨베이어별 최대 3개 |
| 큐 방식 | FIFO |
| 큐 초과 시 | 새로 생산된 물품 폐기, 미적재 감점 대상 |
| 양품/불량품 | Normal / Abnormal |
| 불량률 | 총 생산량의 약 20% |
| 분류 기준 | 색상 센서 기반 검사 |

### 3.2 컨베이어별 생산 주기

| 컨베이어 ID | 생산 주기 |
|---:|---:|
| 1 | 15초 |
| 2 | 18초 |
| 3 | 20초 |
| 4 | 20초 |
| 5 | 30초 |
| 6 | 36초 |
| 7 | 45초 |
| 8 | 45초 |
| 9 | 60초 |
| 10 | 90초 |

빠른 컨베이어일수록 큐가 더 빨리 차므로, 기본적으로 1~4번 컨베이어의 overflow 위험이 크다.

### 3.3 분류 색상

| 종류 | 색상 | Hex |
|---|---|---|
| Normal | 파란색 | `#3140DD` |
| Abnormal | 빨간색 | `#E03636` |

주의: `RealProduct.isNormal` 같은 내부 정답값을 직접 읽으면 안 된다. 반드시 색상 센서 기반으로 판단해야 한다.

---

## 4. 수정 가능 영역과 금지 영역

### 4.1 학생 코드 작성 위치

학생 코드는 아래 폴더에 작성한다.

```text
Assets/CPS_IC_PBL_2026/Scripts/Student/
```

해당 폴더에 새 C# 스크립트를 만들고, 씬의 `StudentControllers` 또는 별도 GameObject에 부착해 기존 로봇/환경 인스턴스를 제어한다.

### 4.2 수정 금지 영역

다음은 원본을 직접 수정하지 않는 것이 원칙이다.

- `[LOCKED] BaseAssets` 하위 원본 스크립트
- 모바일 매니퓰레이터 prefab 원본
- UR5e IK 원본
- EPick/SuctionGripper 원본
- ColorSensor/ColorArea 원본
- 컨베이어/큐/박스 환경 원본
- 채점기/스코어보드/UI 원본

허용되는 방식은 다음이다.

- 새 스크립트를 만들어 씬의 인스턴스에 부착
- 새 GameObject를 만들어 학생 컨트롤러로 사용
- 기존 베이스 컴포넌트를 `SerializeField`로 참조
- 공개 API를 호출해 인스턴스를 제어
- 원본 파일을 바꾸지 않고 외부에서 제어

---

## 5. 레포 기준 사용 가능한 주요 API / 컴포넌트

### 5.1 로봇 이동 API: `IRobotController`

대표 API는 다음과 같다.

```csharp
int RobotId { get; }
Vector3 Position { get; }
bool IsBusy { get; }

void GoToOperatingStation(int stationId);
void MoveBaseTo(Vector3 worldPos, Action onArrived = null);
void MoveArmTo(Vector3 worldPos, Quaternion worldRot, float duration = 1.0f, Action onArrived = null);
```

| API | 용도 |
|---|---|
| `GoToOperatingStation(id)` | 사전 정의된 station으로 모바일 베이스 이동 |
| `MoveBaseTo(worldPos)` | 월드 좌표 기준 베이스 이동 |
| `MoveArmTo(worldPos, worldRot, duration)` | UR5e 팔 TCP를 목표 위치로 이동 |
| `IsBusy` | 로봇 이동/동작 중 여부 확인 |
| `Position` | 현재 로봇 위치 확인 |

주의할 점:

- `RobotId`는 레포 기준 `0 = RobotA`, `1 = RobotB`로 사용한다.
- `IRobotController`는 **low-level 이동 API 중심**이다.
- Pick, Grip, 색상 검사, Box slot 계산은 별도 학생 코드에서 조합해야 한다.
- `MoveArmTo()`의 `worldRot`은 레포 코드상 down-facing IK가 강제되어 무시될 수 있다. 따라서 팔 회전보다 **목표 world position 캘리브레이션**이 중요하다.
- 베이스 이동과 팔 이동을 동시에 호출하지 않는다. `IsBusy == true`인 동안 추가 이동 명령은 무시될 수 있으므로, `IsBusy == false`를 확인한 뒤 다음 명령을 보낸다.

### 5.2 환경 정보 API: `IEnvironmentInfo`

대표 API는 다음과 같다.

```csharp
int GetQueueLength(int conveyorId);
int GetBoxOccupancy(BoxType box);

float CurrentTime { get; }
float ProductionEndTime { get; }
float NextProductionAt(int conveyorId);
```

| API | 용도 |
|---|---|
| `GetQueueLength(1~10)` | 각 컨베이어 큐에 쌓인 물품 수 확인 |
| `GetBoxOccupancy(BoxType box)` | 박스 내 적재 수 확인 |
| `CurrentTime` | 현재 시뮬레이션 시간 |
| `ProductionEndTime` | 생산 종료 시각 |
| `NextProductionAt(id)` | 다음 생산 예정 시각 |

주의할 점:

- 품질 정보는 환경 API로 직접 제공되지 않는다.
- Normal/Abnormal 판정은 ColorSensor/ColorArea 기반으로 처리한다.
- `NextProductionAt()`은 스케줄링 보조 정보로 사용하되, 실제 큐 길이 polling을 우선 신뢰한다.
- 현재 레포 구현에서는 `NextProductionAt()`이 `-1`을 반환할 수 있다. 이 경우 컨베이어 생산 주기 상수와 `GetQueueLength()` polling을 기준으로 스케줄링한다.

### 5.3 Operating Station ID

| Station ID | 의미 |
|---:|---|
| 1~10 | Conveyor 1~10 |
| 100 | Normal Box |
| 101 | Abnormal Box |

레포의 `OperatingStations.asset`에는 각 station의 `BasePosition`, `BaseYawDeg`, `ArmAnchorPoint`가 정의되어 있다. 개발 중 이 값을 기준으로 approach/pick/place pose를 캘리브레이션한다.

| ID | 이름 | BasePosition | BaseYawDeg | ArmAnchorPoint |
|---:|---|---|---:|---|
| 1 | Conveyor_1 | `(-8, 0, -7)` | -90 | `(-9.5, 1.5, -7.6)` |
| 2 | Conveyor_2 | `(-8, 0, -3)` | -90 | `(-9.5, 1.5, -3.6)` |
| 3 | Conveyor_3 | `(-8, 0, 1)` | -90 | `(-9.5, 1.5, 0.4)` |
| 4 | Conveyor_4 | `(-8, 0, 5)` | -90 | `(-9.5, 1.5, 4.4)` |
| 5 | Conveyor_5 | `(-8, 0, 9)` | -90 | `(-9.5, 1.5, 8.4)` |
| 6 | Conveyor_6 | `(-6.5, 0, 10.5)` | 0 | `(-7.1, 1.5, 12)` |
| 7 | Conveyor_7 | `(-2.5, 0, 10.5)` | 0 | `(-3.1, 1.5, 12)` |
| 8 | Conveyor_8 | `(1.5, 0, 10.5)` | 0 | `(0.9, 1.5, 12)` |
| 9 | Conveyor_9 | `(5.5, 0, 10.5)` | 0 | `(4.9, 1.5, 12)` |
| 10 | Conveyor_10 | `(9.5, 0, 10.5)` | 0 | `(8.9, 1.5, 12)` |
| 100 | Normal_Box | `(0, 0, -6)` | 180 | `(0, 0.5, -8)` |
| 101 | Abnormal_Box | `(8.5, 0, 2.5)` | 90 | `(10.5, 0.5, 2.5)` |

### 5.4 색상 센서: `ColorSensor` / `ColorArea`

레포 기준 색상 검사는 `ColorSensor`와 `ColorArea`를 사용한다.

| 컴포넌트 | 역할 |
|---|---|
| `ColorSensor` | 감지 영역의 크기/위치/회전 설정 |
| `ColorArea` | 감지 대상의 renderer 색상을 읽어 `color` 값으로 저장 |

핵심 사용 방식:

```csharp
Color sensed = colorSensor.area.color;
// 또는 ColorArea를 직접 연결한 경우:
Color sensed = colorArea.color;
```

주의할 점:

- 학생은 `ColorSensor` 또는 `ColorArea`를 `[SerializeField]`로 연결할 수 있다. 실제 색상값의 출처는 `ColorArea.color`다.
- 감지 대상이 없으면 `defaultColor`가 나올 수 있다.
- 물품이 센서 영역에 들어오도록 로봇 팔/센서 위치를 맞춰야 한다.
- 색상이 애매하거나 defaultColor면 재검사한다.
- 이름, 태그, 내부 필드로 Normal/Abnormal을 우회 판정하지 않는다.

### 5.5 그리퍼: `SuctionGripper`

대표적으로 다음 기능을 사용한다.

| 기능 | 설명 |
|---|---|
| `TryGrip()` | 조건을 만족하면 후보 물체를 흡착 |
| `Release()` | 들고 있는 물체를 놓음 |
| `IsHolding` | 현재 물체를 들고 있는지 여부 |
| `IsGraspReady` | 현재 grip 가능한 상태인지 여부 |
| `CurrentCandidate` | 현재 후보 물체 |
| `CanGrip(out reason)` | grip 가능 여부와 실패 사유 확인 |

그리퍼는 단순히 `TryGrip()`을 호출한다고 무조건 성공하지 않는다. 다음 조건이 중요하다.

- DetectionTrigger 또는 ContactProbe에 후보 물체가 들어와야 함
- ContactProbe와 실제 접촉해야 함
- 최소 접촉 시간이 필요함
- 상대 속도가 너무 빠르면 실패할 수 있음
- attachPoint와 후보 물체의 거리가 너무 멀면 실패할 수 있음
- 이미 물체를 들고 있으면 실패함
- 실패 원인은 `CanGrip(out reason)`으로 확인해 pose 보정에 활용한다.

따라서 Pick 시퀀스에는 반드시 다음이 포함되어야 한다.

```text
approach pose 이동
→ pick pose 이동
→ 접촉 안정화 대기
→ IsGraspReady 확인
→ TryGrip()
→ IsHolding 확인
→ 실패 시 1~2회 재시도
```

### 5.6 박스 컴포넌트: `BoxTrigger`

박스에는 `BoxTrigger`가 붙어 있으며, 박스 내부 slot 점유 상태를 직접 추적할 때 참조할 수 있다.

| API | 용도 |
|---|---|
| `SlotCount` | 박스가 관리하는 slot 개수 |
| `OccupiedSlotCount` | 등록된 점유 slot 개수 |
| `RegisterSlotPlacement(slotIndex)` | 특정 slot 점유 등록 |
| `IsSlotOccupied(slotIndex)` | 특정 slot 점유 여부 확인 |
| `ClearSlotPlacement(slotIndex)` | 디버그/재시작용 slot 점유 해제 |

주의할 점:

- 물품이 물리적으로 박스 trigger 안에 들어가는 것과 `OccupiedSlotCount`는 같은 의미가 아니다.
- `IEnvironmentInfo.GetBoxOccupancy(BoxType box)`는 현재 레포에서 `BoxTrigger.OccupiedSlotCount`를 반환한다.
- 따라서 `GetBoxOccupancy()`를 slot 검증에 쓰려면 `Palletizer`가 place 성공 시 `RegisterSlotPlacement(slotIndex)` 또는 자체 slot 상태를 일관되게 관리해야 한다.
- 현재 로컬 레포에서는 `PalletGrid` API가 확인되지 않으므로, box 내부 grid slot 좌표는 학생 코드에서 직접 계산하거나 `BoxTrigger`의 slot 점유 API와 함께 관리한다.

---

## 6. 실제 구현 모듈

아래 모듈명은 권장 구조다. 반드시 같은 이름으로 만들 필요는 없지만, 역할은 누락되면 안 된다.

---

### 6.1 FleetManager

#### 역할

전체 시스템의 중앙 관리자.

#### 해야 할 일

- RobotA, RobotB 참조 관리
- EnvironmentInfo 참조 관리
- 매 프레임 또는 일정 주기마다 queue length 확인
- 작업 후보 생성
- TaskAllocator 호출
- idle 상태인 로봇에게 작업 할당
- 컨베이어/박스 예약 상태 관리
- 전체 완료 여부 확인
- 로그/성능 지표 수집

#### 주요 상태

- 로봇별 상태: Idle, Moving, Picking, Inspecting, Placing 등
- 컨베이어별 queue length
- 컨베이어별 예약 여부
- 박스별 예약 여부
- 처리 완료 수
- 실패/재시도 횟수
- 현재 시뮬레이션 시간

---

### 6.2 TaskAllocator

#### 역할

어떤 컨베이어를 먼저 처리할지, 어떤 로봇에게 배정할지 결정한다.

#### 기본 전략

```text
priority =
    queue length score
  + conveyor speed score
  + waiting time score
  + overflow risk score
  - robot distance cost
  - reserved resource penalty
```

#### 권장 우선순위

1. 큐 길이가 3인 컨베이어
2. 큐 길이가 2이면서 생산 주기가 짧은 컨베이어
3. 1~4번처럼 생산 주기가 짧은 컨베이어
4. 오랫동안 처리되지 않은 컨베이어
5. 현재 idle 로봇과 가까운 컨베이어
6. 이미 다른 로봇이 예약한 컨베이어는 제외

#### 구현 포인트

- 큐가 비어 있는 컨베이어에는 작업을 배정하지 않는다.
- 두 로봇이 같은 컨베이어를 동시에 선택하지 않게 한다.
- 작업 배정 시 해당 컨베이어를 예약 처리한다.
- 작업 완료/실패 시 예약을 해제한다.
- RobotA/B 담당 구역을 고정할지, 동적으로 바꿀지 정책화한다.

---

### 6.3 RobotAgent

#### 역할

개별 로봇 1대의 상태 머신.

#### 상태 예시

```text
Idle
→ MoveToConveyor
→ ApproachProduct
→ GripProduct
→ Retract
→ InspectProduct
→ DecideBox
→ MoveToBox
→ PlaceProduct
→ Release
→ Return/Idle
```

#### 해야 할 일

- 로봇 1대의 현재 상태 관리
- `GoToOperatingStation()` 호출
- `MoveArmTo()` 호출
- `SuctionGripper.TryGrip()` / `Release()` 호출
- `ColorSensor.area.color` 또는 `ColorArea.color` 기반 검사 요청
- 작업 성공/실패 이벤트를 FleetManager에 전달
- 실패 시 재시도 또는 작업 포기 처리
- timeout 발생 시 안전 상태로 복귀

---

### 6.4 MissionExecutor

#### 역할

하나의 물품 처리 미션을 순서대로 실행한다.

#### 미션 흐름

```text
1. 컨베이어 Station Lock 획득
2. GoToOperatingStation(conveyorId)
3. 컨베이어 pick용 approach pose로 팔 이동
4. pick pose로 팔 이동
5. 접촉 안정화 대기
6. IsGraspReady 확인
7. TryGrip()
8. IsHolding 확인
9. retract pose로 팔 이동
10. 색상 검사
11. 목적 박스 결정
12. 컨베이어 Lock 해제
13. 박스 Station Lock 획득
14. GoToOperatingStation(100 또는 101)
15. Palletizer에서 다음 place slot 요청
16. place approach pose로 팔 이동
17. 낮은 place pose로 팔 이동
18. Release()
19. 박스 안 적재 여부 확인
20. 박스 Lock 해제
21. 로봇 Idle 복귀
```

#### 실패 처리

| 실패 상황 | 처리 |
|---|---|
| 이동 중 큐가 비어짐 | 작업 취소, lock 해제, 재할당 |
| Grip 실패 | pose 미세 조정 후 1~2회 재시도 |
| 계속 grip 실패 | 실패 로그 남기고 작업 포기 또는 다음 작업 수행 |
| 색상 검사 실패 | 센서 영역 재정렬 후 재검사 |
| 색상 판정 애매함 | 거리 기반 색상 비교, 필요 시 재검사 |
| 박스 lock 획득 실패 | 대기 또는 다른 작업 우선 처리 |
| 로봇 stuck | timeout 후 safe pose 이동 |
| Release 후 박스 밖 이탈 | slot 좌표/높이 재조정 |

---

### 6.5 PoseTable / CalibrationManager

#### 역할

컨베이어별 pick 좌표와 박스별 place 좌표를 관리한다.

이 역할은 실제 구현 난이도가 크다. `GoToOperatingStation()`은 베이스 위치로 이동시킬 뿐이고, 물품을 정확히 집거나 박스 안에 정확히 놓는 좌표는 학생이 맞춰야 한다.

#### 해야 할 일

- 컨베이어 1~10 각각의 approach pose 측정
- 컨베이어 1~10 각각의 pick pose 측정
- pick 후 안전하게 들어 올리는 retract pose 정의
- Normal Box / Abnormal Box의 place approach pose 정의
- 박스 내부 slot별 place pose 정의
- 제품 길이 절반 이하 높이에서 놓을 수 있는 release 높이 조정
- pose가 실패하면 수치 조정 및 로그 기록

#### 권장 pose 구조

```csharp
public class StationPose
{
    public int stationId;
    public Vector3 approachPos;
    public Vector3 actionPos; // conveyor면 pick 위치, box면 place 위치
    public Vector3 retractPos;
}
```

이후 역할 분담 문서와 공통 스키마에서는 `actionPos`를 공통 필드명으로 사용한다.

#### 주의사항

- `ArmAnchorPoint`는 시작점으로 사용하되, 실제 pick/place pose는 Unity에서 테스트하며 보정해야 한다.
- 같은 컨베이어라도 큐의 1번 위치를 정확히 집도록 맞춰야 한다.
- 자유낙하가 발생하지 않도록 place 높이를 낮게 잡아야 한다.
- 물품이나 박스 벽을 통과하는 식으로 좌표를 잡으면 감점 위험이 있다.

---

### 6.6 Sensor / ColorClassifier

#### 역할

ColorSensor/ColorArea 결과를 Normal/Abnormal로 변환한다.

#### 해야 할 일

- 각 로봇의 ColorSensor/ColorArea 참조 연결
- 물품이 센서 영역에 들어오도록 검사 pose 조정
- `ColorSensor.area.color` 또는 `ColorArea.color` 값 읽기
- `#3140DD`와 `#E03636`에 대한 색상 거리 계산
- 가까운 쪽으로 Normal/Abnormal 판정
- defaultColor 또는 애매한 값이면 재검사
- 분류 결과와 실제 적재 box를 로그로 남김

#### 색상 판정 예시

```text
blueDistance = Distance(sensedColor, #3140DD)
redDistance  = Distance(sensedColor, #E03636)

if blueDistance < redDistance:
    Normal
else:
    Abnormal
```

#### 금지사항

- `RealProduct.isNormal` 직접 접근 금지
- 제품 이름, 태그, 내부 필드로 정답 추정 금지
- 채점기 정보로 분류 결과 역추론 금지

---

### 6.7 GripperIntegration

#### 역할

SuctionGripper가 실제로 안정적으로 물품을 잡도록 조건을 맞춘다.

#### 해야 할 일

- DetectionTrigger 후보가 정상적으로 들어오는지 확인
- ContactProbe 접촉이 정상적으로 잡히는지 확인
- `IsGraspReady`가 true가 되는 pose 찾기
- `TryGrip()` 성공률 확인
- `IsHolding`으로 grip 성공 확인
- 실패 원인 로그 확인
- 접촉 시간 부족, 상대 속도 과다, 후보 없음 문제 해결
- Release 후 그리퍼 상태가 초기화되는지 확인

#### 테스트 항목

- [ ] 물품 접근 시 `CurrentCandidate`가 null이 아닌가?
- [ ] pick pose에서 ContactProbe가 접촉하는가?
- [ ] 0.2초 이상 안정화 후 `IsGraspReady`가 true가 되는가?
- [ ] `TryGrip()` 후 `IsHolding`이 true가 되는가?
- [ ] 이동 중 물품이 떨어지지 않는가?
- [ ] `Release()` 후 박스 안에 안정적으로 남는가?

---

### 6.8 ResourceLockManager

#### 역할

로봇 간 자원 경쟁과 충돌을 방지한다.

#### 관리할 자원

| 자원 | Lock 필요 이유 |
|---|---|
| Conveyor 1~10 | 두 로봇이 같은 큐 물품을 동시에 집으려는 상황 방지 |
| Normal Box | 두 로봇이 동시에 같은 박스 앞에서 place하는 상황 방지 |
| Abnormal Box | 동일 |
| 중앙 이동 구역 | RobotA/B가 교차 이동하다 충돌하는 상황 방지 |
| 팔 작업 공간 | 같은 공간에서 팔 동작이 겹치는 상황 방지 |

#### 단순 구현안

- Station 단위 Mutex 사용
- 같은 conveyor station에는 한 번에 한 로봇만 접근
- Normal Box와 Abnormal Box도 각각 한 번에 한 로봇만 접근
- RobotA는 기본적으로 1~5번 우선, RobotB는 6~10번 우선
- 반대편으로 넘어가야 할 때는 중앙 이동 구역 lock 획득
- lock 획득 실패 시 대기하거나 다른 작업 선택

---

### 6.9 PathPlanner / DeadlockGuard

#### 역할

로봇 이동 중 충돌과 deadlock을 줄인다.

ResourceLockManager와 합쳐 구현해도 되지만, 역할상 별도 관리가 필요하다.

#### 해야 할 일

- RobotA/B 기본 담당 구역 정의
- 중앙 crossing 조건 정의
- 두 로봇이 서로 가까워질 때 한쪽 대기
- 박스 앞 동시 접근 금지
- lock을 여러 개 잡을 때 순서 통일
- 두 로봇이 서로 lock을 기다리며 멈추는 deadlock 방지
- 일정 시간 이상 대기하면 lock 해제/재계획

#### 단순 정책 예시

```text
RobotA: Conveyor 1~5 우선
RobotB: Conveyor 6~10 우선

단, 한쪽 큐가 비었고 반대쪽 큐가 위험하면 교차 작업 허용.
교차 작업 시 centralLock을 먼저 획득한 로봇만 이동.
Box 접근은 Normal/Abnormal 각각 boxLock을 얻은 로봇만 수행.
```

---

### 6.10 Palletizer

#### 역할

Normal/Abnormal 박스 안에 물품을 안정적으로 정렬 배치한다.

#### 해야 할 일

- Normal Box slot index 관리
- Abnormal Box slot index 관리
- 다음에 놓을 좌표 계산
- 물품끼리 겹치지 않게 배치
- 박스 벽을 통과하지 않게 배치
- 제품 길이 절반 이하 높이에서 내려놓기
- 자유낙하 없이 낮은 위치에서 `Release()`
- `GetBoxOccupancy()`와 자체 slot index가 크게 어긋나지 않는지 확인
- 현재 로컬 레포에 `PalletGrid`가 없으면 자체 grid 좌표 계산을 사용

#### 단순 slot 구조 예시

```text
Normal Box:
[0,0], [1,0], [2,0], ...
[0,1], [1,1], [2,1], ...

Abnormal Box:
[0,0], [1,0], [2,0], ...
```

초기 구현은 1층 grid 배치를 우선한다. 64개 전체 적재에 공간이 부족하면 2층 적재를 검토한다.

---

### 6.11 Telemetry / DebugLogger

#### 역할

성능 개선과 오류 분석을 위한 로그 수집.

#### 기록할 내용

- 시간별 queue length
- 로봇별 상태 변화
- 작업 할당 내역
- station lock 획득/해제
- 중앙 구역 lock 대기 시간
- grip 성공/실패
- grip 실패 원인
- 색상 검사 결과
- Normal/Abnormal 판정 결과
- 박스별 slot 사용 현황
- 충돌/대기/timeout 발생 여부
- 최종 완료 시간
- 미적재 추정 수

---

### 6.12 Build / QA / Submission

#### 역할

최종 제출물이 정상적으로 만들어지는지 관리한다.

#### 해야 할 일

- Unity 2022.3.x LTS 사용 확인
- 팀원 Unity 버전 통일
- Git 브랜치/머지 충돌 관리
- `[LOCKED] BaseAssets` 원본 수정 여부 확인
- 씬 reference missing 여부 확인
- Console compile error 0개 유지
- Windows Standalone 빌드 생성
- 3배속 시연 영상 촬영
- 5분 이내 발표 영상 준비
- 발표자료 PPTX 준비
- 최종 보고서 정리
- Unity 프로젝트 zip 생성
- 제출 전 빌드 파일 실행 검증

---

## 7. 역할 분담안

아래는 역할 분담을 위한 권장안이다. 팀원 수에 따라 합쳐도 되지만, **역할 자체는 모두 커버되어야 한다.**

---

### 역할 1. Fleet / Scheduling 담당

#### 담당 모듈

- `FleetManager`
- `TaskAllocator`

#### 주요 책임

- 전체 시스템 흐름 설계
- queue length polling
- 컨베이어별 작업 후보 생성
- 작업 우선순위 정책 설계
- RobotA/B 작업 할당
- 컨베이어 예약 상태 관리
- 완료 시간 최적화

#### 산출물

- `FleetManager.cs`
- `TaskAllocator.cs`
- queue priority 정책 문서 또는 코드 주석
- 성능 로그: 작업 할당 순서, 완료 시간

---

### 역할 2. Robot Mission / Motion 담당

#### 담당 모듈

- `RobotAgent`
- `MissionExecutor`

#### 주요 책임

- 로봇 1대의 상태 머신 구현
- `GoToOperatingStation()` 호출 흐름 구현
- `MoveArmTo()` 기반 팔 이동 시퀀스 구현
- Pick → Inspect → Place 전체 미션 흐름 구현
- 실패 재시도 로직 구현
- timeout 및 안전 복귀 처리

#### 산출물

- `RobotAgent.cs`
- `MissionExecutor.cs`
- 단일 로봇 Pick & Place 성공 테스트
- RobotA/B 각각 단독 처리 테스트

---

### 역할 3. Pose / Calibration / Gripper 협업 담당

#### 담당 모듈

- `PoseTable`
- `CalibrationManager`
- `GripperIntegration` 또는 4인 Slice 기준 `GripperAdapter`

#### 주요 책임

- 컨베이어별 approach/pick/retract pose 측정
- 박스별 place approach/place pose 측정
- `OperatingStations`의 `ArmAnchorPoint`를 기준으로 좌표 보정
- SuctionGripper의 DetectionTrigger/ContactProbe 조건 확인
- `TryGrip()` 성공률 개선
- 자유낙하 없는 release 높이 조정

#### 산출물

- `PoseTable.cs`
- `CalibrationManager.cs`
- 컨베이어별 pick pose 표
- 박스별 place slot 기준 좌표
- grip 실패 원인별 대응 로그

4인 Slice 기준에서는 GripperIntegration의 실제 구현 파일을 Slice B의 `Robot/GripperAdapter.cs`로 둔다. Slice C는 grip 성공률을 높이기 위한 pick pose 보정과 실패 원인 분석에 협업한다.

---

### 역할 4. Vision / Classification 담당

#### 담당 모듈

- `ColorClassifier`
- Sensor 연결 코드

#### 주요 책임

- RobotA/B의 ColorSensor/ColorArea 참조 연결
- `ColorSensor.area.color` 또는 `ColorArea.color` 읽기
- Normal/Abnormal 색상 거리 기반 분류
- defaultColor 또는 애매한 색상 재검사
- `RealProduct.isNormal` 직접 접근 없이 분류
- 분류 오류 최소화

#### 산출물

- `ColorClassifier.cs`
- 색상 거리 판정 함수
- 색상 검사 성공/실패 로그
- Normal/Abnormal 분류 테스트 결과

---

### 역할 5. Collision / Resource Lock / Path 담당

#### 담당 모듈

- `ResourceLockManager`
- `PathPlanner`
- `DeadlockGuard`

#### 주요 책임

- conveyor station lock 구현
- Normal/Abnormal box lock 구현
- 중앙 이동 구역 lock 구현
- RobotA/B 담당 구역 정책 설계
- 충돌 회피 정책 구현
- deadlock 방지 로직 구현
- lock 대기 시간 최소화

#### 산출물

- `ResourceLockManager.cs`
- `PathPlanner.cs`
- lock 정책 문서 또는 코드 주석
- 충돌 0회 검증 로그

---

### 역할 6. Palletizing / Evaluation / QA / Submission 담당

#### 담당 모듈

- `Palletizer`
- `TelemetryLogger`
- 제출물 관리

#### 주요 책임

- Normal/Abnormal 박스 slot 관리
- 박스 내부 grid 배치 좌표 계산
- 물품 간 겹침 방지
- 박스 벽 통과 방지
- 물리 점수 항목 점검
- 성능 로그 수집
- 빌드/영상/보고서/PPT 제출 관리

#### 산출물

- `Palletizer.cs`
- `TelemetryLogger.cs`
- 박스별 slot 사용 현황
- 최종 성능 측정 결과
- 발표자료, 보고서, 시연 영상, 빌드 zip, 프로젝트 zip

4인 Slice 기준에서는 `Palletizer.cs`는 Slice C의 `Pose/` 디렉터리에서 담당하고, `TelemetryLogger.cs`와 Bootstrap/scene reference는 Slice D가 담당한다. 빌드/영상/보고서/PPT 제출 검증은 Slice D 단독 책임이 아니라 팀 공통 후반 작업으로 관리한다.

---

### 7.1 팀원이 5명인 경우 권장 병합

5명 팀이라면 아래처럼 합치는 것을 권장한다.

| 팀원 | 권장 역할 |
|---|---|
| 1번 | Fleet / Scheduling |
| 2번 | Robot Mission / Motion |
| 3번 | Pose / Calibration / Gripper |
| 4번 | Vision / Classification + 일부 Sensor 연결 |
| 5번 | Collision / Resource Lock / Path + Palletizing / QA |

단, Palletizing이 어려워지면 3번과 5번이 함께 맡는 것이 좋다. Pick/Place pose와 Palletizer는 좌표 보정이 많아서 서로 강하게 연결된다.

### 7.2 팀원이 4명인 경우 권장 병합

| 팀원 | 권장 역할 |
|---|---|
| 1번 | Fleet / Scheduling |
| 2번 | Robot Mission / Motion + Gripper |
| 3번 | Pose / Calibration + Palletizing |
| 4번 | Vision / Classification + Collision / Lock + Telemetry / Bootstrap + QA 조율 |

---

## 8. 구현 우선순위

### P0. 프로젝트 실행 및 참조 연결

- Unity 2022.3.x LTS에서 프로젝트 열기
- 씬 정상 실행 확인
- Console compile error 0개 만들기
- RobotA, RobotB 참조 연결
- EnvironmentInfo 참조 연결
- ColorSensor/ColorArea 참조 연결
- SuctionGripper 참조 연결
- Student 폴더에 테스트용 스크립트 생성

### P1. Queue 상태 읽기

- `GetQueueLength(1~10)` 값 출력
- `CurrentTime`, `ProductionEndTime` 출력
- 각 컨베이어 생산 주기와 실제 queue 변화 확인
- queue length 0인 컨베이어 제외 처리

### P2. 단일 로봇 이동 테스트

- RobotA를 `GoToOperatingStation(1)`로 이동
- RobotA를 `GoToOperatingStation(100)`으로 이동
- RobotB도 동일 테스트
- `IsBusy` 상태 확인

### P3. Pick Pose / Gripper 성공

- 컨베이어 1개 기준 approach/pick/retract pose 측정
- ContactProbe 접촉 확인
- `IsGraspReady` true 확인
- `TryGrip()` 성공
- `IsHolding` true 확인
- retract 후 물품이 떨어지지 않는지 확인

### P4. Place Pose / Release 성공

- Normal Box 기준 place pose 측정
- 낮은 높이에서 `Release()`
- 물품이 박스 안에 남는지 확인
- 자유낙하/튕김/박스 밖 이탈 여부 확인

### P5. 색상 검사 및 분류

- ColorSensor/ColorArea가 물품 색상을 읽는지 확인
- Normal/Abnormal 판정 함수 구현
- defaultColor 재검사 처리
- Normal Box / Abnormal Box 분기 이동

### P6. 단일 로봇 전체 미션 성공

- RobotA 단독으로 queue 감시 → 이동 → pick → 검사 → 분류 → place 수행
- RobotB 단독으로 동일 수행
- 실패 재시도 로직 추가

### P7. 2대 로봇 작업 할당

- idle 로봇에게 자동 작업 할당
- 같은 컨베이어 중복 배정 방지
- conveyor reservation 적용
- queue length 3 또는 빠른 컨베이어 우선 처리

### P8. Resource Lock / Collision 회피

- Conveyor lock 적용
- Normal/Abnormal box lock 적용
- 중앙 이동 구역 lock 적용
- RobotA/B 담당 구역 정책 적용
- 충돌 감점 0회 목표

### P9. Palletizing 정렬

- 박스별 slot 좌표 계산
- Normal/Abnormal slot index 관리
- 박스 occupancy와 slot index 비교
- 물품 겹침/박스 밖 이탈 방지
- 정렬도 개선

### P10. 성능 최적화

- 전체 완료 시간 측정
- 미적재 수 추정
- 분류 오류 확인
- lock 대기 시간 분석
- 이동 거리 줄이기
- 빠른 컨베이어 우선순위 조정
- 목표: 64개 전부 적재, 분류 오류 0개, 충돌 0회, 가능하면 250초 이하

### P11. 제출 준비

- Windows Standalone 빌드
- 3배속 시연 영상
- 5분 이내 발표 영상
- 발표자료 PPTX
- 최종 보고서
- Unity 프로젝트 zip
- 제출 전 빌드 실행 검증

---

## 9. 권장 개발 순서

```text
1. Unity 프로젝트 정상 실행
2. Student 폴더에 FleetManager 테스트 스크립트 생성
3. RobotA/B, EnvironmentInfo, ColorSensor/ColorArea, SuctionGripper 참조 연결
4. 1~10번 queue length 콘솔 출력
5. RobotA를 1번 컨베이어 station으로 이동
6. RobotA 팔을 approach/pick/retract pose로 이동
7. SuctionGripper로 물품 1개 grip 성공
8. Normal Box로 이동 후 낮은 높이에서 release
9. ColorSensor/ColorArea 기반 색상 검사 추가
10. Normal/Abnormal box 분기 이동
11. RobotB에도 동일 미션 적용
12. TaskAllocator로 자동 작업 선택
13. ResourceLockManager로 중복 작업/충돌 방지
14. Palletizer로 slot 기반 정렬 배치
15. 180초 전체 시뮬레이션 반복 테스트
16. 시간/미적재/분류오류/충돌 로그 기반 최적화
17. 빌드와 제출물 검증
```

---

## 10. 평가 기준에 맞춘 성공 조건

### 10.1 성능 점수 구조

팀프로젝트 점수 중 성능 점수는 다음 구조로 평가된다.

```text
성능 점수 = 시간 점수(1~10점) + 팔레타이징·물리 점수(1~5점)
```

감점 항목:

| 감점 항목 | 감점 |
|---|---:|
| 분류 오류 | 개당 -1점 |
| 자원/로봇 충돌 | 회당 -1점, 상한 -5점 |
| 박스 미적재 | 개당 완료 시간 +5초 |

### 10.2 시간 점수

64개 물품이 모두 박스에 적재 완료된 시각으로 측정된다. 미적재 물품이 있으면 1개당 5초가 추가된다.

| 완료 시간 | 점수 |
|---:|---:|
| 250초 이하 | 10점 |
| 250~265초 | 9점 |
| 265~280초 | 8점 |
| 280~295초 | 7점 |
| 295~310초 | 6점 |
| 310~325초 | 5점 |
| 325~340초 | 4점 |
| 340~355초 | 3점 |
| 355~370초 | 2점 |
| 370초 초과 또는 미완료 | 1점 |

### 10.3 팔레타이징/물리 점수

다음 항목을 종합 평가한다.

- 64개 중 박스 안에 들어간 비율
- 정렬도
- 자유낙하 없음
- 벽 통과 없음
- 순간이동 없음
- 제품 길이 절반 이하 높이에서 내려놓기
- 박스 안 적재 자세 안정성

---

## 11. 구현 시 금지사항

아래 방식은 과제 의도에 어긋나거나 감점 가능성이 크다.

- `[LOCKED] BaseAssets` 원본 스크립트/프리팹 직접 수정
- `RealProduct.isNormal` 직접 읽기
- 제품 이름/태그/내부 필드로 Normal/Abnormal 우회 판정
- 채점기/스코어 관련 코드 수정
- 물품 순간이동
- 로봇 순간이동
- 로봇이 벽이나 박스를 통과하도록 이동
- 한 번에 여러 물품 들기
- 박스 위에서 물품을 떨어뜨리는 자유낙하 방식
- 두 로봇이 같은 컨베이어/박스 위치를 동시에 점유
- 물리 시뮬레이션을 우회해서 점수만 올리는 방식

---

## 12. 최소 완성 MVP

최소 완성 기준은 다음이다.

- [ ] Unity 프로젝트가 정상 실행된다.
- [ ] Console compile error가 없다.
- [ ] Student 폴더의 코드만으로 동작한다.
- [ ] RobotA/B 2대가 모두 움직인다.
- [ ] `GetQueueLength(1~10)`로 큐 상태를 읽는다.
- [ ] queue가 있는 컨베이어를 자동 선택한다.
- [ ] 물품을 1개씩 집는다.
- [ ] `ColorSensor.area.color` 또는 `ColorArea.color` 기반으로 Normal/Abnormal을 분류한다.
- [ ] Normal은 Normal Box에 넣는다.
- [ ] Abnormal은 Abnormal Box에 넣는다.
- [ ] 두 로봇이 같은 컨베이어를 중복 처리하지 않는다.
- [ ] 두 로봇이 같은 박스 앞에 동시에 접근하지 않는다.
- [ ] 명백한 충돌 없이 동작한다.
- [ ] 자유낙하 없이 낮은 높이에서 물품을 놓는다.
- [ ] 64개 전체 또는 최대한 많은 물품을 처리한다.

---

## 13. 고득점 개선 방향

### 13.1 스케줄링 최적화

- 큐 길이 3 도달 전 선제 처리
- 생산 주기가 짧은 컨베이어 우선
- `NextProductionAt()`을 보조 지표로 활용
- 로봇 현재 위치와 이동 거리 반영
- RobotA/B 담당 구역 유동 조정
- overflow 위험 점수 반영
- lock 대기 시간이 긴 작업은 우선순위 조정

### 13.2 Motion / Pose 최적화

- approach/pick/retract pose 최소 이동
- 팔 이동 duration 조정
- grip 안정화 대기 시간 최적화
- 불필요한 station 이동 제거
- box place pose를 slot별로 단순화

### 13.3 충돌 회피 개선

- 구역 분할: RobotA는 좌측, RobotB는 우측 우선
- 중앙 crossing 시 lock 적용
- 박스 앞 동시 접근 금지
- 서로 가까워질 경우 한 로봇 대기
- lock 획득 순서 통일로 deadlock 방지

### 13.4 팔레타이징 개선

- 박스 내부 grid slot 정렬
- 적재 높이 제어
- 물품 회전 자세 통일
- 박스 occupancy와 자체 slot index 동기화
- Normal/Abnormal 예상 개수에 맞춰 slot 배치 여유 확보

### 13.5 안정성 개선

- Grip 실패 재시도
- 센서 검사 실패 재검사
- 작업 중 queue가 비어졌을 때 fallback
- 로봇 stuck 상태 감지
- mission timeout 처리
- 로그 기반 실패 원인 추적

---

## 14. 테스트 체크리스트

### 14.1 기본 실행 테스트

- [ ] Unity 2022.3.x LTS에서 열린다.
- [ ] Console compile error가 없다.
- [ ] 씬 실행 시 채점기/UI가 정상 표시된다.
- [ ] RobotA/B reference가 missing이 아니다.
- [ ] EnvironmentInfo reference가 missing이 아니다.
- [ ] ColorSensor/ColorArea reference가 missing이 아니다.
- [ ] SuctionGripper reference가 missing이 아니다.

### 14.2 Queue / Scheduling 테스트

- [ ] `GetQueueLength(1~10)` 값이 정상 출력된다.
- [ ] queue가 0인 컨베이어는 작업 후보에서 제외된다.
- [ ] queue가 3인 컨베이어가 높은 우선순위를 가진다.
- [ ] 1~4번 빠른 컨베이어가 적절히 우선 처리된다.
- [ ] 같은 컨베이어가 두 로봇에 중복 배정되지 않는다.
- [ ] 작업 완료/실패 후 예약이 해제된다.

### 14.3 Motion / Gripper 테스트

- [ ] `GoToOperatingStation(1~10)`이 각 컨베이어 앞으로 이동한다.
- [ ] `GoToOperatingStation(100/101)`이 각 박스 앞으로 이동한다.
- [ ] 팔이 approach pose에 도달한다.
- [ ] 팔이 pick pose에 도달한다.
- [ ] `CurrentCandidate`가 정상 감지된다.
- [ ] ContactProbe 접촉이 잡힌다.
- [ ] `IsGraspReady`가 true가 된다.
- [ ] `TryGrip()` 후 `IsHolding`이 true가 된다.
- [ ] retract 중 물품이 떨어지지 않는다.
- [ ] `Release()` 후 물품이 박스 안에 남는다.

### 14.4 Vision / Classification 테스트

- [ ] ColorSensor/ColorArea가 물품 색상을 읽는다.
- [ ] 감지 대상이 없을 때 defaultColor가 나오는지 확인했다.
- [ ] 파란색 물품이 Normal로 분류된다.
- [ ] 빨간색 물품이 Abnormal로 분류된다.
- [ ] 애매한 색상 또는 defaultColor일 때 재검사한다.
- [ ] `RealProduct.isNormal`에 접근하지 않는다.
- [ ] 분류 오류가 발생하지 않는다.

### 14.5 Collision / Lock 테스트

- [ ] 같은 conveyor station에 두 로봇이 동시에 접근하지 않는다.
- [ ] Normal Box 앞에 두 로봇이 동시에 접근하지 않는다.
- [ ] Abnormal Box 앞에 두 로봇이 동시에 접근하지 않는다.
- [ ] 중앙 이동 구역에서 충돌하지 않는다.
- [ ] lock 대기 중 deadlock이 발생하지 않는다.
- [ ] 충돌 감점이 발생하지 않는다.

### 14.6 Palletizing 테스트

- [ ] Normal Box slot index가 정상 증가한다.
- [ ] Abnormal Box slot index가 정상 증가한다.
- [ ] 물품끼리 겹치지 않는다.
- [ ] 물품이 박스 밖으로 나가지 않는다.
- [ ] 물품이 박스 벽을 통과하지 않는다.
- [ ] 자유낙하 없이 낮은 높이에서 놓는다.
- [ ] 정렬도가 유지된다.

### 14.7 전체 통합 테스트

- [ ] RobotA 단독으로 물품 1개 처리 가능
- [ ] RobotB 단독으로 물품 1개 처리 가능
- [ ] RobotA/B가 동시에 다른 작업 처리 가능
- [ ] 180초 전체 생산 시뮬레이션 수행
- [ ] 64개 전체 또는 최대한 많은 물품 처리
- [ ] 미적재 물품 최소화
- [ ] 분류 오류 0개 목표
- [ ] 충돌 0회 목표
- [ ] 최종 완료 시간 기록

---

## 15. 최종 제출물

15주차 최종 제출물은 다음이다.

- 발표자료 `PPTX`
- 발표 영상 `mp4`  
  - 조별 5분 이내
- 최종 보고서
- 시연 영상 `mp4`  
  - 3배속 촬영
- Windows Standalone 빌드 파일 `zip`
- Unity 프로젝트 파일 `zip`  
  - 사후 검증용

보고서 구성은 자유 양식이지만, 다음 구성이 적절하다.

```text
1. 서론
2. 문제 정의
3. 제안 방법
4. 시스템 구조
5. 작업 할당 정책
6. 충돌 회피 및 Lock 정책
7. 색상 검사 및 분류 방법
8. Pick/Place Pose 및 팔레타이징 방법
9. 성능 평가
10. 결과 분석
11. 한계 및 개선 방향
12. 결론
13. 참고 자료
14. 팀원별 역할 및 소감
```

---

## 16. 최종 요약

이 과제에서 팀이 해야 할 일은 **Unity 씬을 새로 만드는 것**이 아니라, 이미 제공된 디지털 트윈 환경과 로봇 API 위에 다음 제어 지능을 얹는 것이다.

```text
Queue 감시
→ 작업 우선순위 계산
→ RobotA/B 작업 할당
→ Station/Box/Path Lock 획득
→ 로봇 이동
→ Pick pose 접근
→ Gripper 접촉 조건 확인
→ 물품 Grip
→ ColorSensor/ColorArea 기반 색상 검사
→ Normal/Abnormal 분류
→ Box 이동
→ Palletizer slot 계산
→ 낮은 높이에서 안정적 Place
→ Lock 해제
→ 다음 작업 반복
```

따라서 개발의 중심은 다음 모듈이다.

```text
FleetManager
TaskAllocator
RobotAgent
MissionExecutor
PoseTable / CalibrationManager
GripperAdapter / GripperIntegration
ColorClassifier
ResourceLockManager
PathPlanner / DeadlockGuard
Palletizer
TelemetryLogger
Build / QA / Submission
```

역할 분담 시에는 단순히 “로봇 움직이기”, “스케줄링”만 나누면 안 된다. 실제 구현 난이도가 높은 **Pick/Place 좌표 보정, Gripper 조건 검증, ColorSensor 연결, 박스 slot 배치, 충돌/Deadlock 방지, 빌드/제출 검증**까지 반드시 담당자를 지정해야 한다.
