// 导出管线:HTML(内核 exportContent)、PDF/打印(WebKit 打印管线)、长图(全文档快照)。
// 分片数学与 PNG 落盘在此;GTK 交互在窗口层。

/// 长图按高度分片的切片高度序列(总高 2500、片高 1000 → [1000,1000,500])。
pub fn slice_heights(total_height: u32, slice_height: u32) -> Vec<u32> {
    if total_height == 0 || slice_height == 0 {
        return Vec::new();
    }
    let full = total_height / slice_height;
    let remainder = total_height % slice_height;
    let mut slices = vec![slice_height; full as usize];
    if remainder > 0 {
        slices.push(remainder);
    }
    slices
}

#[cfg(test)]
mod tests {
    use super::slice_heights;

    #[test]
    fn slice_math_boundaries() {
        assert_eq!(slice_heights(2500, 1000), vec![1000, 1000, 500]);
        assert_eq!(slice_heights(2000, 1000), vec![1000, 1000]);
        assert_eq!(slice_heights(999, 1000), vec![999]);
        assert_eq!(slice_heights(0, 1000), Vec::<u32>::new());
        assert_eq!(slice_heights(100, 0), Vec::<u32>::new());
    }
}
