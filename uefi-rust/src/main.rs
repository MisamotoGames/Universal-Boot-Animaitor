#![no_main]
#![no_std]

extern crate alloc;
use alloc::vec::Vec;
use uefi::prelude::*;
use uefi::boot::ScopedProtocol;
use uefi::proto::BootPolicy;
use uefi::proto::console::gop::{BltOp, BltPixel, BltRegion, GraphicsOutput};
use uefi::proto::device_path::{build::{self, DevicePathBuilder}, DevicePath};
use uefi::proto::loaded_image::LoadedImage;
use uefi::proto::media::fs::SimpleFileSystem;
use uefi::proto::media::file::{Directory, File, FileAttribute, FileMode, RegularFile};
use uefi::CString16;
use uefi::boot::SearchType;
use uefi::Identify;
use core::time::Duration;
use zune_jpeg::JpegDecoder;
use zune_jpeg::zune_core::{bytestream::ZCursor, colorspace::ColorSpace, options::DecoderOptions};

fn read_u32(b: &[u8], p: usize) -> Option<usize> { Some(u32::from_le_bytes(b.get(p..p+4)?.try_into().ok()?) as usize) }

fn open_relative(root: &mut Directory, path: &str) -> Option<RegularFile> {
    let path = CString16::try_from(path).ok()?;
    root.open(path.as_ref(), FileMode::Read, FileAttribute::empty()).ok()?.into_regular_file()
}

fn find_animation_file() -> Option<(ScopedProtocol<SimpleFileSystem>, RegularFile)> {
    let handles = boot::locate_handle_buffer(SearchType::ByProtocol(&SimpleFileSystem::GUID)).ok()?;
    let mut legacy = None;
    for handle in handles.iter() {
        let Ok(mut protocol) = boot::open_protocol_exclusive::<SimpleFileSystem>(*handle) else { continue; };
        let Ok(mut root) = protocol.open_volume() else { continue; };
        if let Some(animation) = open_relative(&mut root, "BAS\\animation.bas") { return Some((protocol, animation)); }
        if legacy.is_none() {
            if let Some(animation) = open_relative(&mut root, "EFI\\BAS\\animation.bas") {
                legacy = Some((protocol, animation));
            }
        }
    }
    legacy
}

fn load_windows_boot_manager(image: Handle) -> uefi::Result<Handle> {
    let loaded_image = boot::open_protocol_exclusive::<LoadedImage>(image)?;
    let device = loaded_image.device().ok_or(uefi::Status::NOT_FOUND)?;
    drop(loaded_image);

    let device_path = boot::open_protocol_exclusive::<DevicePath>(device)?;
    let path = CString16::try_from("\\EFI\\Microsoft\\Boot\\bootmgfw.efi")
        .map_err(|_| uefi::Status::INVALID_PARAMETER)?;
    let mut storage = Vec::new();
    let mut builder = DevicePathBuilder::with_vec(&mut storage);
    for node in device_path.node_iter() {
        if node.is_end_entire() { break; }
        builder = builder.push(&node).map_err(|_| uefi::Status::OUT_OF_RESOURCES)?;
    }
    let file_node = build::media::FilePath { path_name: &path };
    let full_path = builder
        .push(&file_node)
        .and_then(DevicePathBuilder::finalize)
        .map_err(|_| uefi::Status::OUT_OF_RESOURCES)?;
    boot::load_image(image, boot::LoadImageSource::FromDevicePath {
        device_path: full_path,
        boot_policy: BootPolicy::BootSelection,
    })
}

fn decode_jpeg(jpeg: &[u8], expected_w: usize, expected_h: usize, rgb: &mut Vec<u8>) -> bool {
    let options = DecoderOptions::default().jpeg_set_out_colorspace(ColorSpace::RGB);
    let mut decoder = JpegDecoder::new_with_options(ZCursor::new(jpeg), options);
    if decoder.decode_headers().is_err() { return false; }
    let Some(info) = decoder.info() else { return false; };
    let width = usize::from(info.width);
    let height = usize::from(info.height);
    let expected_len = width.saturating_mul(height).saturating_mul(3);
    if width != expected_w || height != expected_h || width == 0 || height == 0 || width > 8192 || height > 8192 || width.saturating_mul(height) > 4_194_304 || decoder.output_buffer_size() != Some(expected_len) { return false; }
    rgb.resize(expected_len, 0);
    decoder.decode_into(rgb).is_ok()
}

fn draw_rgb(gop: &mut GraphicsOutput, rgb: &[u8], width: usize, height: usize, pixels: &mut Vec<BltPixel>) -> bool {
    let (screen_width, screen_height) = gop.current_mode_info().resolution();
    if width > screen_width || height > screen_height { return false; }
    pixels.resize(width * height, BltPixel::new(0, 0, 0));
    for (i, p) in pixels.iter_mut().enumerate() {
        let k = i * 3;
        *p = BltPixel::new(rgb[k], rgb[k + 1], rgb[k + 2]);
    }
    let dest = ((screen_width - width) / 2, (screen_height - height) / 2);
    gop.blt(BltOp::BufferToVideo { buffer: &pixels, src: BltRegion::Full, dest, dims: (width, height) }).is_ok()
}

fn read_exact(file: &mut RegularFile, buffer: &mut [u8]) -> bool {
    let mut offset = 0;
    while offset < buffer.len() {
        let Ok(n) = file.read(&mut buffer[offset..]) else { return false; };
        if n == 0 { return false; }
        offset += n;
    }
    true
}

fn play_lqa(animation_file: &mut RegularFile) {
    let mut header = [0u8; 20];
    if !read_exact(animation_file, &mut header) || &header[0..8] != b"BASANIM1" { return; }
    let Some(version) = read_u32(&header, 8) else { return; };
    let Some(fps) = read_u32(&header, 12) else { return; };
    let Some(frame_count) = read_u32(&header, 16) else { return; };
    if version != 1 || !(1..=60).contains(&fps) || !(1..=600).contains(&frame_count) { return; }
    let frame_timer = unsafe { boot::create_event(boot::EventType::TIMER, boot::Tpl::APPLICATION, None, None) }.ok();
    if let Ok(handle) = boot::get_handle_for_protocol::<GraphicsOutput>() {
      if let Ok(mut gop) = boot::open_protocol_exclusive::<GraphicsOutput>(handle) {
        let (screen_width, screen_height) = gop.current_mode_info().resolution();
        let _ = gop.blt(BltOp::VideoFill {
            color: BltPixel::new(0, 0, 0),
            dest: (0, 0),
            dims: (screen_width, screen_height),
        });
        let mut rgb = Vec::new();
        let mut pixels = Vec::new();
        let mut frame_header = [0u8; 12];
        let mut jpeg = Vec::new();
        for i in 0..frame_count {
            if !read_exact(animation_file, &mut frame_header) { break; }
            let Some(width) = read_u32(&frame_header, 0) else { break; };
            let Some(height) = read_u32(&frame_header, 4) else { break; };
            let Some(length) = read_u32(&frame_header, 8) else { break; };
            if width == 0 || height == 0 || width > 8192 || height > 8192 || width.saturating_mul(height) > 4_194_304 { break; }
            if length == 0 || length > 64 * 1024 * 1024 { break; }
            jpeg.resize(length, 0);
            let mut scheduled_timer = None;
            if i > 0 {
                if let Some(timer) = frame_timer.as_ref() {
                    if boot::set_timer(timer, boot::TimerTrigger::Relative(Duration::from_micros(1_000_000 / fps as u64))).is_ok() {
                        scheduled_timer = Some(timer);
                    }
                }
            }
            // Start the frame deadline before decoding. If decode runs past it, draw as soon as ready;
            // otherwise wait for the deadline. The cadence never bursts to catch up.
            if !read_exact(animation_file, &mut jpeg) || !decode_jpeg(&jpeg, width, height, &mut rgb) { break; }
            if i > 0 {
                if let Some(timer) = scheduled_timer {
                    if boot::wait_for_event(core::slice::from_ref(timer)).is_err() {
                        boot::stall(Duration::from_micros(1_000_000 / fps as u64));
                    }
                } else {
                    boot::stall(Duration::from_micros(1_000_000 / fps as u64));
                }
            }
            if !draw_rgb(&mut gop, &rgb, width, height, &mut pixels) { break; }
        }
      }
    }
    if let Some(timer) = frame_timer { let _ = boot::close_event(timer); }
}

#[entry]
fn main() -> Status {
    uefi::helpers::init().unwrap();
    let image = boot::image_handle();
    // A missing animation disk must never prevent the existing Windows boot path.
    if let Some((_animation_fs, mut animation_file)) = find_animation_file() {
        play_lqa(&mut animation_file);
    }
    match load_windows_boot_manager(image) {
        Ok(h) => boot::start_image(h).map(|_| Status::SUCCESS).unwrap_or(Status::LOAD_ERROR),
        Err(e) => e.status(),
    }
}
