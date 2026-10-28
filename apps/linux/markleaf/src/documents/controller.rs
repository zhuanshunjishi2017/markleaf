// DocumentController 的纯逻辑决策部分。
// 生命周期接线(bridge↔store↔recovery 的 GTK 侧编排)在窗口/标签层(M3);
// 此处沉淀可无头单测的决策:编码保留策略、恢复快照防抖。
use std::time::{Duration, Instant};

use encoding_rs::Encoding;

use super::encoding::EncodingDetection;

/// 保存编码决策:保留文档原编码与 BOM(默认 UTF-8 无 BOM)。
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct EncodingDecision {
    pub encoding: &'static Encoding,
    pub bom: bool,
}

impl EncodingDecision {
    pub fn from_detection(detection: &EncodingDetection) -> Self {
        Self {
            encoding: detection.encoding,
            bom: detection.had_bom,
        }
    }
}

/// 恢复快照防抖:dirty 后按间隔落盘,不每 keystroke 写一次。
#[derive(Debug)]
pub struct RecoveryDebounce {
    interval: Duration,
    last_snapshot: Option<Instant>,
}

impl RecoveryDebounce {
    pub fn new(interval: Duration) -> Self {
        Self {
            interval,
            last_snapshot: None,
        }
    }

    /// dirty 变化时询问是否该落盘;落盘后调用 mark_written。
    pub fn should_snapshot(&self, now: Instant) -> bool {
        match self.last_snapshot {
            None => true,
            Some(last) => now.duration_since(last) >= self.interval,
        }
    }

    pub fn mark_written(&mut self, now: Instant) {
        self.last_snapshot = Some(now);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::time::Duration;

    #[test]
    fn encoding_decision_preserves_original_encoding_and_bom() {
        // GBK 文件:保存回 GB18030(encoding_rs 的 GBK 是 GB18030 别名族)。
        let gbk: [u8; 8] = [0xD6, 0xD0, 0xCE, 0xC4, 0xB2, 0xE2, 0xCA, 0xD4];
        let detection = crate::documents::encoding::detect(&gbk);
        let decision = EncodingDecision::from_detection(&detection);
        assert!(!decision.bom);
        assert!(decision.encoding.name().contains("GB"), "实得 {}", decision.encoding.name());

        // BOM 文件:BOM 保留。
        let mut bytes = vec![0xEF, 0xBB, 0xBF];
        bytes.extend_from_slice("正文".as_bytes());
        let detection = crate::documents::encoding::detect(&bytes);
        let decision = EncodingDecision::from_detection(&detection);
        assert!(decision.bom);
        assert_eq!(decision.encoding.name(), "UTF-8");
    }

    #[test]
    fn recovery_debounce_first_immediate_then_interval() {
        let mut debounce = RecoveryDebounce::new(Duration::from_secs(3));
        let t0 = Instant::now();
        assert!(debounce.should_snapshot(t0), "首次立即落盘");
        debounce.mark_written(t0);
        assert!(!debounce.should_snapshot(t0 + Duration::from_secs(2)));
        assert!(debounce.should_snapshot(t0 + Duration::from_secs(3)));
    }
}
