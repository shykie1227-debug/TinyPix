use std::sync::atomic::{AtomicBool, Ordering};

pub struct AppState {
    pub log_started: bool,
    pub cancel_requested: AtomicBool,
}

impl AppState {
    pub fn new() -> Self {
        Self {
            log_started: false,
            cancel_requested: AtomicBool::new(false),
        }
    }

    pub fn request_cancel(&self) {
        self.cancel_requested.store(true, Ordering::SeqCst);
    }

    pub fn is_cancel_requested(&self) -> bool {
        self.cancel_requested.load(Ordering::SeqCst)
    }

    pub fn reset_cancel(&self) {
        self.cancel_requested.store(false, Ordering::SeqCst);
    }
}

pub fn configure_tracing() {
    let filter = tracing_subscriber::EnvFilter::try_from_default_env()
        .unwrap_or_else(|_| tracing_subscriber::EnvFilter::new("info"));
    tracing_subscriber::fmt().with_env_filter(filter).init();
}
