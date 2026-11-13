// DocumentController 决策测试(编码保留/恢复防抖)。
use std::time::{Duration, Instant};

use markleaf::documents::controller::{EncodingDecision, RecoveryDebounce};

#[test]
fn encoding_decision_preserves_original() {
    let gbk: [u8; 8] = [0xD6, 0xD0, 0xCE, 0xC4, 0xB2, 0xE2, 0xCA, 0xD4];
    let detection = markleaf::documents::encoding::detect(&gbk);
    let decision = EncodingDecision::from_detection(&detection);
    assert!(decision.encoding.name().contains("GB"), "实得 {}", decision.encoding.name());
    assert!(!decision.bom);
}

#[test]
fn recovery_debounce_first_immediate_then_interval() {
    let mut debounce = RecoveryDebounce::new(Duration::from_secs(3));
    let t0 = Instant::now();
    assert!(debounce.should_snapshot(t0));
    debounce.mark_written(t0);
    assert!(!debounce.should_snapshot(t0 + Duration::from_secs(2)));
    assert!(debounce.should_snapshot(t0 + Duration::from_secs(3)));
}
