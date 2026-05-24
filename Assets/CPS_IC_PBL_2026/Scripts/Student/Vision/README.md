# Vision

## 담당 슬라이스

Slice D: Vision / Safety / Telemetry / Bootstrap

## 이 디렉터리의 책임

`ColorArea.color` 값을 받아 물품이 Normal인지 Abnormal인지 분류한다. 분류는 반드시 색상 센서 기반으로 해야 하며, `RealProduct.isNormal` 같은 내부 정답값을 읽으면 안 된다.

## 생성할 코드 파일

```text
ColorClassifier.cs
```

## 파일별 작업

- `ColorClassifier.cs`: `IColorClassifier` 구현, sensed color를 `ColorClassificationResult`로 변환

## 입력

- Robot/MissionExecutor가 전달하는 `ColorArea.color`
- 기준 색상:
  - Normal: `#3140DD`
  - Abnormal: `#E03636`

## 출력

- `ColorClassificationResult.productClass`
- `sensedColor`
- `blueDistance`
- `redDistance`
- `reliable`
- `message`

## 판정 기준

1. sensed color와 Normal 기준색 `#3140DD`의 RGB 거리를 계산한다.
2. sensed color와 Abnormal 기준색 `#E03636`의 RGB 거리를 계산한다.
3. blue distance가 더 작으면 Normal로 판정한다.
4. red distance가 더 작으면 Abnormal로 판정한다.
5. default color에 가깝거나 두 거리 차이가 너무 작으면 `Unknown`, `reliable=false`로 반환한다.

## 다른 슬라이스와의 연결

- Robot은 직접 색상 판정 로직을 구현하지 않고 `IColorClassifier.Classify()`만 호출한다.
- Telemetry에는 sensed color, 판정 결과, reliable 여부, distance 값을 남길 수 있게 한다.
- Bootstrap은 RobotA/B 각각의 `ColorArea` reference를 연결한다.

## 구현 순서

1. hex 색상을 Unity `Color` 값으로 변환하는 기준을 정한다.
2. RGB distance 계산 함수를 만든다.
3. Normal/Abnormal/Unknown 판정 로직을 만든다.
4. default color 또는 ambiguous color를 `reliable=false`로 처리한다.
5. RobotA/B의 ColorArea에서 실제 색상이 읽히는지 확인한다.

## 완료 기준

- `#3140DD`에 가까운 색상을 Normal로 판정한다.
- `#E03636`에 가까운 색상을 Abnormal로 판정한다.
- default color 또는 애매한 색상은 `reliable=false`가 된다.
- `RealProduct.isNormal`, 제품 이름, 태그, 채점기 정보를 사용하지 않는다.
- RobotA/B 양쪽에서 같은 classifier를 사용할 수 있다.

## 주의사항

- 신뢰도가 낮은 색상은 즉시 확정하지 말고 Robot 쪽에서 재검사 또는 mission fail 흐름으로 넘긴다.
- 색상 판정 기준을 임의로 자주 바꾸면 Robot/Telemetry 분석이 어려워지므로 threshold는 상수화한다.
