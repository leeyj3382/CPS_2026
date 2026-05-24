# Telemetry

## 담당 슬라이스

Slice D: Vision / Safety / Telemetry / Bootstrap

## 이 디렉터리의 책임

작업 생성, 배정, mission 결과, lock, grip, 색상 분류, slot 상태 같은 실행 로그를 기록한다. 통합 테스트와 성능 튜닝의 기준 데이터를 만든다.

## 생성할 코드 파일

```text
TelemetryLogger.cs
```

## 파일별 작업

- `TelemetryLogger.cs`: `ITelemetryLogger` 구현, 각 슬라이스에서 호출할 logging API 제공

## 기록할 이벤트

- task 생성, 할당, 완료, 실패
- RobotA/B state 변화
- mission start/result/failure reason
- queue length 변화 또는 polling 결과
- lock acquire, release, fail, timeout
- grip ready, grip success/fail, holding 상태
- color classification 결과, sensed color, reliable 여부
- slot reserve, commit, release
- stuck, timeout, deadlock 의심 상황
- 최종 완료 시간과 처리 개수

## 다른 슬라이스와의 연결

- Fleet는 task 생성/할당/mission result를 기록한다.
- Robot은 state, grip, mission 진행/실패를 기록한다.
- Vision은 색상 판정 결과를 기록할 수 있게 값을 제공한다.
- Safety는 lock acquire/release/fail을 기록한다.
- Pose/Palletizer는 slot reserve/commit/release와 occupancy mismatch를 기록한다.

## 로그 형식 기준

- category를 일관되게 사용한다. 예: `Task`, `Robot`, `Lock`, `Grip`, `Color`, `Slot`, `Mission`
- message에는 robot id, task id, conveyor id, station id 중 필요한 값을 포함한다.
- 너무 많은 per-frame 로그는 피한다.
- 180초 전체 시뮬레이션에서 실패 원인을 추적할 수 있을 정도로 남긴다.

## 구현 순서

1. `ITelemetryLogger` interface에 맞는 기본 logger를 만든다.
2. `Debug.Log` 기반으로 시작한다.
3. Fleet task 생성/할당/result 로그부터 연결한다.
4. Safety lock 로그를 연결한다.
5. Robot grip/mission failure 로그를 연결한다.
6. Vision classification과 Pose slot 로그를 연결한다.

## 완료 기준

- task가 언제 생성/할당/완료/실패했는지 추적할 수 있다.
- lock 대기와 실패 원인을 볼 수 있다.
- grip 실패와 색상 분류 실패를 구분할 수 있다.
- slot reserve/commit/release 흐름을 확인할 수 있다.
- 최종 성능 분석에 필요한 완료 시간과 처리 개수를 확인할 수 있다.

## 주의사항

- TelemetryLogger는 점수 조작용이 아니라 디버깅/분석용이다.
- 로그가 너무 많아 시뮬레이션을 느리게 만들지 않도록 한다.
- 최종 제출물 정리는 팀 공통 후반 작업이며, Telemetry는 성능 분석 자료를 제공한다.
