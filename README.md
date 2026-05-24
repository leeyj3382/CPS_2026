# CPS 2026 IC-PBL 작업 가이드

이 프로젝트는 제공된 Unity 디지털 트윈 환경에서 RobotA/B 2대를 제어해, 10개 컨베이어의 물품을 pick, color inspect, classify, place, palletize하는 과제다.

코드는 아래 경로에만 작성한다.

```text
Assets/CPS_IC_PBL_2026/Scripts/Student/
```

`Assets/CPS_IC_PBL_2026/[LOCKED] BaseAssets/` 하위 원본 파일은 수정하지 않는다.

---

## 문서 읽는 순서

처음 시작할 때는 루트의 문서를 아래 순서로 읽는다.

```text
1. CPS_2026_TaskOverview.md
2. CPS_2026_RoleSlice.md
3. WorkDirectory.md
4. 자신이 맡은 디렉터리의 README.md
```

각 문서의 역할은 다음과 같다.

| 문서                       | 용도                                                 |
| -------------------------- | ---------------------------------------------------- |
| `CPS_2026_TaskOverview.md` | 전체 과제 목표, 환경, API, 금지사항, 평가 기준       |
| `CPS_2026_RoleSlice.md`    | 4인 Slice 역할 분배, 공통 schema, Slice 간 호출 흐름 |
| `WorkDirectory.md`         | Slice별 작업 디렉터리와 생성해야 할 코드 파일 목록   |
| `Student/*/README.md`      | 각 디렉터리에서 실제로 구현해야 할 작업 가이드       |

---

## 브랜치 작업 방식

개발 범위가 크지 않으므로 Slice별로 feature 브랜치를 하나씩 파서 작업한다. Git 브랜치명에는 공백을 넣지 않는 것이 안전하므로 아래 이름을 사용한다.

```text
feat/slice-A
feat/slice-B
feat/slice-C
feat/slice-D
```

담당별 브랜치와 작업 범위는 다음과 같다.

| 담당 Slice | 브랜치         | 작업 디렉터리                                                                    |
| ---------- | -------------- | -------------------------------------------------------------------------------- |
| Slice A    | `feat/slice-A` | `Student/Common/`, `Student/Fleet/`                                              |
| Slice B    | `feat/slice-B` | `Student/Robot/`                                                                 |
| Slice C    | `feat/slice-C` | `Student/Pose/`                                                                  |
| Slice D    | `feat/slice-D` | `Student/Bootstrap/`, `Student/Vision/`, `Student/Safety/`, `Student/Telemetry/` |

브랜치 생성 예시는 다음과 같다.

```bash
git checkout -b feat/slice-A
```

작업 원칙:

- `main` 브랜치에서 직접 작업하지 않는다.
- 각자 자신의 Slice 브랜치에서 작업한다.
- 작업이 끝나면 PR을 열어 `main`에 merge한다.
- PR을 열기 전 Unity compile error와 자신이 맡은 README의 완료 기준을 확인한다.
- 다른 Slice 디렉터리는 가능한 한 수정하지 않는다.
- `Common` schema는 전체 Slice가 의존하므로 먼저 합의하고 반영한다.
- Unity scene 파일은 충돌이 잦으므로 Bootstrap/scene reference 담당자 또는 팀장이 관리한다.
- Slice별 구현이 어느 정도 끝나면 통합 브랜치에서 합치고 RobotA 단일 테스트부터 진행한다.

---

## GitHub 사용법

팀원이 GitHub를 처음 쓰는 경우 아래 순서대로 진행한다. 명령어는 프로젝트 루트 디렉터리에서 실행한다.

### 1. 처음 프로젝트 받기

프로젝트 폴더가 아직 없다면 `git clone`으로 받는 것이 가장 안전하다.

```bash
git clone https://github.com/leeyj3382/CPS_2026.git
cd CPS_2026
```

받은 뒤 현재 브랜치를 확인한다.

```bash
git branch
git status
```

### 2. 이미 폴더가 있는 경우 저장소 연결

이미 프로젝트 폴더를 받은 상태인데 GitHub 저장소와 연결되어 있지 않다면 다음 순서로 진행한다.

```bash
git init
git remote add origin https://github.com/leeyj3382/CPS_2026.git
git fetch origin
git checkout -B main origin/main
```

주의: `git checkout -B main origin/main`은 로컬 `main`을 원격 `main` 기준으로 다시 맞추는 명령이다. 이미 작업한 파일이 있거나 commit하지 않은 변경사항이 있으면 먼저 `git status`로 확인한다.

연결이 잘 되었는지 확인한다.

```bash
git remote -v
git branch
```

### 3. 작업 시작 전 main 최신화

작업을 시작하기 전에는 항상 로컬 `main`을 최신 상태로 만든다.

```bash
git checkout main
git pull origin main
```

### 4. 개인 Slice 브랜치 생성

`main`을 최신화한 뒤 자기 Slice 브랜치를 만든다.

```bash
git checkout -b feat/slice-A
```

이미 브랜치가 있는 경우에는 새로 만들지 말고 이동한다.

```bash
git checkout feat/slice-A
```

### 5. 개발 후 커밋

작업 후 변경 파일을 확인한다.

```bash
git status
```

변경한 파일을 stage에 올리고 commit한다.

```bash
git add Assets/CPS_IC_PBL_2026/Scripts/Student/
git commit -m "feat: implement slice A work"
```

작업한 모든 변경사항을 한 번에 올리려면 `git add .`를 사용할 수 있다.

```bash
git add .
git commit -m "feat: implement slice A work"
```

단, `git add .`는 현재 폴더 아래 모든 변경사항을 stage에 올린다. commit 전에 반드시 `git status`로 의도하지 않은 파일, scene 파일, `[LOCKED] BaseAssets` 변경사항이 포함되지 않았는지 확인한다.

커밋 메시지는 너무 길게 쓰지 않아도 되지만, 어떤 작업인지 알 수 있게 적는다.

예시:

```text
feat: add common schema
feat: add robot mission skeleton
feat: add pose table
fix: release lock on mission failure
docs: update slice guide
```

### 6. 개인 브랜치 push

처음 push할 때는 `-u` 옵션을 붙인다.

```bash
git push -u origin feat/slice-A
```

이후 같은 브랜치에서 추가 커밋을 push할 때는 아래처럼 해도 된다.

```bash
git push
```

### 7. Pull Request 만들기

GitHub 웹사이트에서 다음 순서로 PR을 만든다.

1. GitHub 저장소로 이동한다.
2. `Compare & pull request`를 누른다.
3. base branch가 `main`인지 확인한다.
4. compare branch가 자신의 `feat/slice-*` 브랜치인지 확인한다.
5. 작업 내용과 테스트 여부를 간단히 적는다.
6. PR을 생성한다.
7. 팀원 확인 후 `main`에 merge한다.

PR 제목 예시:

```text
feat: implement slice A common and fleet skeleton
feat: implement slice B robot mission flow
feat: implement slice C pose and palletizer
feat: implement slice D bootstrap safety telemetry
```

### 8. 작업 중 main 변경사항 가져오기

다른 팀원의 PR이 먼저 merge되면, 내 브랜치에도 최신 `main`을 반영해야 한다.

```bash
git checkout main
git pull origin main
git checkout feat/slice-A
git merge main
```

충돌이 나면 충돌 파일을 직접 수정한 뒤 아래 순서로 마무리한다.

```bash
git status
git add <충돌 해결한 파일>
git commit
git push
```

### 9. Unity 협업 주의사항

Unity 프로젝트는 일반 코드 프로젝트보다 파일 참조가 깨지기 쉽다. 아래 규칙을 지킨다.

- `.meta` 파일은 반드시 관련 asset/script와 같이 커밋한다.
- `Library/`, `Temp/`, `Obj/`, `Build/`, `Logs/` 폴더는 커밋하지 않는다.
- scene 파일(`.unity`)은 한 명만 수정한다.
- prefab 원본은 가능하면 수정하지 않는다.
- Inspector reference 연결이 필요하면 scene 담당자에게 요청한다.
- Unity에서 파일을 이동하거나 이름을 바꿀 때는 Finder가 아니라 Unity Editor 안에서 처리한다.
- `[LOCKED] BaseAssets` 하위 원본 파일은 수정하지 않는다.

### 10. PR 전 체크리스트

PR을 열기 전에 아래를 확인한다.

- [ ] 현재 브랜치가 `main`이 아닌지 확인했다.
- [ ] `git status`로 의도하지 않은 파일이 없는지 확인했다.
- [ ] `[LOCKED] BaseAssets` 하위 파일이 변경되지 않았는지 확인했다.
- [ ] `.unity` scene 파일을 실수로 수정하지 않았는지 확인했다.
- [ ] 필요한 `.meta` 파일이 같이 포함되어 있는지 확인했다.
- [ ] Unity Console compile error가 없는지 확인했다.
- [ ] 내가 맡은 Slice README의 완료 기준을 확인했다.

### 11. GitHub 작업 시 주의사항

- `main`에서 직접 개발하지 않는다.
- 작업 전에는 항상 `main`을 최신화한다.
- 개인 작업은 반드시 `feat/slice-A`, `feat/slice-B`, `feat/slice-C`, `feat/slice-D` 중 자기 브랜치에서 한다.
- PR 없이 `main`에 바로 merge하지 않는다.
- Unity scene 파일은 충돌이 자주 나므로 한 명만 수정한다.
- `[LOCKED] BaseAssets` 하위 파일이 변경사항에 들어가면 PR 전에 반드시 확인한다.
- commit 전에 `git status`로 의도하지 않은 파일이 들어갔는지 확인한다.

---

## AI Agent를 쓰는 경우

AI agent에게는 문서를 읽는 순서와 본인의 Slice를 명확히 알려줘야 한다. 한 번에 전체 구현을 맡기기보다, 작은 작업 단위로 나눠서 진행한다.

### 권장 진행 순서

1. agent에게 `CPS_2026_TaskOverview.md`를 먼저 읽게 한다.
2. 이어서 `CPS_2026_RoleSlice.md`를 읽게 한다.
3. `WorkDirectory.md`에서 본인 Slice의 작업 디렉터리와 파일명을 확인하게 한다.
4. 본인 Slice의 디렉터리 README를 읽게 한다.
5. agent에게 본인 역할 기준으로 작업 목록을 뽑게 한다.
6. 작업을 하나씩 구현하게 한다.
7. 구현 후 compile/test 기준을 확인하게 한다.

### 예시 프롬프트

Slice A 담당자:

```text
CPS_2026_TaskOverview.md -> CPS_2026_RoleSlice.md -> WorkDirectory.md 순서로 읽고,
나는 Slice A 담당자야.
Assets/CPS_IC_PBL_2026/Scripts/Student/Common/README.md와
Assets/CPS_IC_PBL_2026/Scripts/Student/Fleet/README.md를 읽은 뒤,
내가 구현해야 할 파일 목록과 작업 순서를 정리해줘.
먼저 Common schema부터 하나씩 구현하자.
```

Slice B 담당자:

```text
CPS_2026_TaskOverview.md와 CPS_2026_RoleSlice.md를 먼저 읽고,
나는 Slice B 담당자야.
Student/Robot/README.md 기준으로 RobotAgent, MissionExecutor, GripperAdapter를 구현해야 해.
RobotA/B 두 인스턴스에서 재사용 가능한 구조로 작업 목록을 쪼개줘.
```

Slice C 담당자:

```text
과제 개요와 역할 분배 문서를 읽고,
나는 Slice C 담당자야.
Student/Pose/README.md 기준으로 PoseTable, CalibrationManager, Palletizer 구현 순서를 알려줘.
StationPose는 actionPos 기준으로 맞춰줘.
```

Slice D 담당자:

```text
CPS_2026_TaskOverview.md -> CPS_2026_RoleSlice.md -> WorkDirectory.md 순서로 읽고,
나는 Slice D 담당자야.
Bootstrap, Vision, Safety, Telemetry README를 읽은 뒤
내가 먼저 만들어야 할 skeleton과 scene reference 연결 순서를 알려줘.
```

### Agent 사용 시 주의사항

- agent에게 `[LOCKED] BaseAssets`를 수정하지 말라고 명확히 지시한다.
- 한 번에 전체 Slice를 구현시키기보다 파일 하나 또는 기능 하나씩 맡긴다.
- 공통 schema 변경은 팀원 전체와 합의한 뒤 진행한다.
- 구현 후에는 agent에게 “문서 기준과 충돌하는 부분이 있는지” 다시 점검하게 한다.
- Unity scene reference 연결은 한 명이 관리해 scene merge conflict를 줄인다.

---

## AI Agent를 쓰지 않는 경우

agent 없이 직접 작업할 때도 문서 읽는 순서는 같다.

### 권장 진행 순서

1. 루트의 `CPS_2026_TaskOverview.md`를 읽고 전체 과제 목표와 금지사항을 이해한다.
2. `CPS_2026_RoleSlice.md`를 읽고 4인 Slice 구조와 공통 schema를 확인한다.
3. `WorkDirectory.md`에서 자신이 담당하는 디렉터리와 파일명을 확인한다.
4. 자신이 맡은 디렉터리의 `README.md`를 읽는다.
5. README의 구현 순서와 완료 기준에 따라 파일을 하나씩 만든다.
6. 다른 Slice와 연결되는 부분은 `StudentInterfaces.cs`의 interface를 기준으로 맞춘다.
7. 기능 구현 후 Unity에서 compile error와 missing reference를 확인한다.

### 역할별 읽을 README

| 담당 Slice | 읽을 README                                                                                                          |
| ---------- | -------------------------------------------------------------------------------------------------------------------- |
| Slice A    | `Student/Common/README.md`, `Student/Fleet/README.md`                                                                |
| Slice B    | `Student/Robot/README.md`                                                                                            |
| Slice C    | `Student/Pose/README.md`                                                                                             |
| Slice D    | `Student/Bootstrap/README.md`, `Student/Vision/README.md`, `Student/Safety/README.md`, `Student/Telemetry/README.md` |

### 직접 구현 시 주의사항

- `Common`의 enum/schema/interface를 먼저 맞춘다.
- `StationPose`는 `actionPos` 필드명을 사용한다.
- RobotA/B는 같은 Robot 실행 코드를 두 인스턴스에서 재사용하는 구조로 만든다.
- Fleet reservation과 Safety lock을 혼동하지 않는다.
- 색상 분류는 `ColorSensor.area.color` 또는 `ColorArea.color` 기반으로만 한다.
- `RealProduct.isNormal`, 제품 이름, 태그, 채점기 정보로 분류를 우회하지 않는다.
- pick/place 좌표는 Unity 실행 테스트로 보정한다.

---

## 구현 방식 요약

현재 문서 기준의 제어 방식은 다음과 같다.

### 모바일 베이스 이동

베이스 이동은 station 기반이다.

```text
GoToOperatingStation(1~10)   -> Conveyor station
GoToOperatingStation(100)    -> Normal Box station
GoToOperatingStation(101)    -> Abnormal Box station
```

즉, RobotA/B가 컨베이어나 박스 앞으로 가는 이동은 `OperatingStations.asset`에 정의된 station을 기준으로 한다.

베이스 이동과 팔 이동을 동시에 명령하지 않는다. `IRobotController.IsBusy == false`가 된 뒤 다음 이동 명령을 보낸다.

### 팔 pick/place 이동

팔 동작은 좌표 기반이다.

```text
approachPos -> actionPos -> retractPos
```

- conveyor에서는 `actionPos`가 실제 pick 위치다.
- box에서는 `placePos`가 실제 place 위치다.
- 좌표는 world position 기준으로 캘리브레이션한다.
- `MoveArmTo()`의 `worldRot`은 레포 기준 down-facing IK 때문에 사실상 무시되므로, 회전보다 위치 보정이 중요하다.

### Palletizing

팔레타이징은 box 내부 slot 좌표 기반이다.

```text
ReserveNextSlot(BoxType boxType, int robotId, int taskId)
-> slot.approachPos
-> slot.placePos
-> Release()
-> CommitSlot()
```

따라서 이 프로젝트는 “전체 경로를 waypoint graph로 짜는 방식”이 아니라, **베이스는 station 이동, 팔과 팔레타이징은 보정된 world 좌표/slot 좌표를 사용하는 방식**으로 진행한다.

---

## 최종 통합 체크

- [ ] Common schema compile 완료
- [ ] RobotA/B scene reference 연결 완료
- [ ] queue length `1~10` 읽기 가능
- [ ] RobotA 단일 mission 성공
- [ ] RobotB 단일 mission 성공
- [ ] ColorSensor/ColorArea 기반 Normal/Abnormal 분류
- [ ] Normal Box / Abnormal Box slot place 성공
- [ ] 같은 conveyor/box/central/arm zone 중복 점유 방지
- [ ] 180초 전체 시뮬레이션 반복 테스트
- [ ] 빌드, 시연 영상, 보고서, 발표자료 준비

---

제가 유니티 프로젝트는 처음이라, 실수한 부분이 있을 수 있습니다.  
문서나 구조에서 이상한 부분이 보이면 언제든 알려주세요!!
