// --- STATE & UTILS ---
let cart = JSON.parse(localStorage.getItem('cart') || '[]');

const formatPrice = (price) => {
    return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(price);
};

// --- INITIALIZATION ---
document.addEventListener('DOMContentLoaded', () => {
    // 1. Remove Loader
    setTimeout(() => {
        const loader = document.getElementById('loader');
        if (loader) {
            loader.style.opacity = '0';
            setTimeout(() => loader.style.display = 'none', 300);
        }
        initGSAP();
    }, 1000);

    // 2. Render UI
    renderProductsList(products);
    renderTestimonials();
    updateCartUI();

    // 3. Khởi tạo hiệu ứng 3D
    console.log('Initializing 3D Effects...');
    initThreeJS();

    // 4. Header Scroll Effect
    window.addEventListener('scroll', () => {
        const navbar = document.getElementById('navbar');
        if (!navbar) return;
        if (window.scrollY > 50) {
            navbar.classList.add('bg-header-scrolled');
            navbar.classList.remove('bg-header-light');
        } else {
            navbar.classList.add('bg-header-light');
            navbar.classList.remove('bg-header-scrolled');
        }
    });

    // 5. Mobile Menu
    const mobileMenu = document.getElementById('mobile-menu');
    const mobileMenuBtn = document.getElementById('mobile-menu-btn');
    const closeMobileBtn = document.getElementById('close-mobile');

    if (mobileMenu && mobileMenuBtn) {
        mobileMenuBtn.addEventListener('click', () => {
            mobileMenu.classList.remove('translate-x-full');
        });
    }
    if (mobileMenu && closeMobileBtn) {
        closeMobileBtn.addEventListener('click', () => {
            mobileMenu.classList.add('translate-x-full');
        });
    }
    document.querySelectorAll('.mobile-link').forEach(link => {
        link.addEventListener('click', () => {
            if (mobileMenu) mobileMenu.classList.add('translate-x-full');
        });
    });

    // 6. Cart Sidebar
    const cartSidebar = document.getElementById('cart-sidebar');
    const cartOverlay = document.getElementById('cart-overlay');
    const cartIcon = document.getElementById('cart-icon');
    const closeCartBtn = document.getElementById('close-cart');

    if (cartIcon && cartSidebar && cartOverlay) {
        cartIcon.addEventListener('click', openCart);
        if (closeCartBtn) closeCartBtn.addEventListener('click', closeCart);
        cartOverlay.addEventListener('click', closeCart);
    }

    function openCart() {
        cartSidebar.classList.remove('translate-x-full');
        cartOverlay.classList.remove('hidden');
        cartOverlay.style.opacity = '1';
    }

    function closeCart() {
        cartSidebar.classList.add('translate-x-full');
        cartOverlay.style.opacity = '0';
        setTimeout(() => cartOverlay.classList.add('hidden'), 300);
    }

    // Checkout Fake Alert
    const checkoutBtn = document.getElementById('checkout-btn');
    if (checkoutBtn) {
        checkoutBtn.addEventListener('click', () => {
            if (cart.length === 0) {
                alert('Giỏ hàng trống! Vui lòng chọn món ăn.');
                return;
            }
            // Navigate to actual checkout flow
            window.location.href = '/Order/Checkout';
        });
    }

    // Search Logic
    const searchInput = document.getElementById('searchInput');
    if (searchInput) {
        searchInput.addEventListener('input', (e) => {
            const query = e.target.value.toLowerCase();
            if (query === '') {
                renderProducts('all');
            } else {
                const filtered = products.filter(p => p.name.toLowerCase().includes(query));
                renderProductsList(filtered);
            }
        });
    }
});

// --- RENDER FUNCTIONS ---
function renderTopBranches(branchesData) {
    const container = document.getElementById('top-branches-list');
    if (!container) return;
    container.innerHTML = '';

    if (!branchesData || branchesData.length === 0) {
        container.innerHTML = '<p class="text-brand-gray col-span-full">Không tìm thấy nhà hàng nào.</p>';
        return;
    }

    branchesData.forEach(branch => {
        const card = document.createElement('div');
        card.className = 'w-[80vw] md:w-[350px] flex-shrink-0 product-card bg-brand-black rounded-2xl p-4 flex flex-col border border-gray-800 relative z-10 transition-transform group cursor-pointer hover:border-brand-orange/50 hover:shadow-[0_0_20px_rgba(255,107,0,0.15)]';
        
        card.innerHTML = `
            <div class="h-48 w-full mb-4 rounded-xl overflow-hidden relative">
                <div class="absolute top-2 right-2 bg-black/60 backdrop-blur-md text-white text-xs font-bold px-2.5 py-1 rounded-md z-40 flex items-center gap-1 border border-white/10">
                    <i class="fa-solid fa-star text-brand-orange text-[10px]"></i> ${branch.rating.toFixed(1)}
                </div>
                <img src="${branch.image}" alt="${branch.name}" class="cat-img w-full h-full object-cover opacity-80 group-hover:opacity-100 transition-all duration-500 transform group-hover:scale-105">
            </div>
            <div class="px-2 pb-2">
                <h3 class="font-bold text-xl mb-1 truncate text-white">${branch.name}</h3>
                <p class="text-brand-gray text-sm mb-4 line-clamp-1">${branch.address}</p>
                
                <div class="flex items-center justify-between border-t border-gray-800 pt-4 mt-auto">
                    <span class="text-brand-orange text-sm font-bold flex items-center gap-1.5"><i class="fa-solid fa-fire text-brand-orange"></i> Đã bán: ${branch.totalSold}</span>
                    <a href="/Home/Store/${branch.id}" class="w-10 h-10 rounded-full bg-brand-orange/10 text-brand-orange flex items-center justify-center hover:bg-brand-orange hover:text-white transition-colors">
                        <i class="fa-solid fa-arrow-right"></i>
                    </a>
                </div>
            </div>
        `;
        
        // No routing requested, just static link
        container.appendChild(card);
    });

    // Add drag to scroll logic
    let isDown = false;
    let startX;
    let scrollLeft;

    container.addEventListener('mousedown', (e) => {
        isDown = true;
        container.style.cursor = 'grabbing';
        startX = e.pageX - container.offsetLeft;
        scrollLeft = container.scrollLeft;
    });
    container.addEventListener('mouseleave', () => {
        isDown = false;
        container.style.cursor = 'grab';
    });
    container.addEventListener('mouseup', () => {
        isDown = false;
        container.style.cursor = 'grab';
    });
    container.addEventListener('mousemove', (e) => {
        if (!isDown) return;
        e.preventDefault();
        const x = e.pageX - container.offsetLeft;
        const walk = (x - startX) * 2; // Scroll-fast multiplier
        container.scrollLeft = scrollLeft - walk;
    });
    container.style.cursor = 'grab';
}

function renderProducts(categoryId) {
    let list = products;
    if (categoryId !== 'all') {
        list = products.filter(p => p.categoryId === categoryId);
    }
    renderProductsList(list);
}

function renderProductsList(list) {
    const container = document.getElementById('product-list');
    if (!container) return;
    container.innerHTML = '';

    if (list.length === 0) {
        container.innerHTML = '<p class="text-brand-gray col-span-full">Không tìm thấy món ăn.</p>';
        return;
    }

    list.forEach(p => {
        const wrap = document.createElement('div');
        wrap.className = 'product-card-container section-title';

        wrap.innerHTML = `
            <div class="product-card bg-brand-black rounded-2xl p-4 h-full flex flex-col border border-gray-800 relative z-10 overflow-visible transition-all duration-300 hover:-translate-y-2 hover:shadow-[0_15px_30px_rgba(0,0,0,0.6)] hover:border-gray-600">
                <div class="absolute top-4 right-4 bg-black px-2 py-1 rounded bg-opacity-70 text-sm font-bold flex items-center gap-1 z-20">
                    <i class="fa-solid fa-star text-yellow-500"></i> ${p.rating}
                </div>
                <div class="flex-1 min-h-[12rem] w-full mb-4 rounded-xl overflow-hidden relative">
                    <img src="${p.image}" alt="${p.name}" class="absolute inset-0 w-full h-full object-cover opacity-90 transition-transform duration-500 hover:scale-105">
                </div>
                <div class="px-2 pb-2 mt-auto">
                    <h3 class="font-bold text-xl mb-1 truncate text-white">${p.name}</h3>
                    <p class="text-brand-gray text-xs mb-4 line-clamp-2">${p.description}</p>
                    <div class="flex items-center justify-between pt-4 border-t border-gray-800/80">
                        <div>
                            <span class="text-xl font-black text-brand-orange leading-none block">${formatPrice(p.price)}</span>
                            ${p.totalSold !== undefined ? `<span class="text-[10px] font-bold text-brand-gray mt-1 block w-full"><i class="fa-solid fa-fire text-brand-orange mr-1"></i> Đã mua: ${p.totalSold}</span>` : ''}
                        </div>
                        <button onclick="addToCart(${p.id})" class="add-to-cart-btn w-10 h-10 rounded-full bg-brand-orange text-black flex items-center justify-center hover:bg-yellow-500 transition-colors">
                            <i class="fa-solid fa-plus"></i>
                        </button>
                    </div>
                </div>
            </div>
        `;
        container.appendChild(wrap);
    });

    if (typeof ScrollTrigger !== 'undefined') ScrollTrigger.refresh();
}

function renderTestimonials() {
    const container = document.getElementById('testimonial-slider');
    if (!container) return;
    testimonials.forEach(t => {
        const el = document.createElement('div');
        el.className = 'min-w-[300px] md:min-w-[350px] bg-gray-900 border border-gray-800 p-6 rounded-2xl flex-shrink-0 snap-start';
        el.innerHTML = `
            <div class="flex items-center gap-4 mb-4">
                <img src="${t.avatar}" class="w-14 h-14 rounded-full border-2 border-brand-orange">
                <div>
                    <h4 class="font-bold">${t.name}</h4>
                    <div class="text-yellow-500 text-sm">
                        ${Array(t.rating).fill('<i class="fa-solid fa-star"></i>').join('')}
                    </div>
                </div>
            </div>
            <p class="text-brand-gray italic">"${t.comment}"</p>
        `;
        container.appendChild(el);
    });
}

// --- CART LOGIC ---
window.addToCart = function (productId) {
    let product = null;
    if (window.products) {
        product = window.products.find(p => p.id === productId);
    }
    if (!product && typeof products !== 'undefined') {
        product = products.find(p => p.id === productId);
    }

    if (!product) {
        console.error("Product not found!", productId);
        return;
    }

    // Single Store Enforcement
    if (cart.length > 0 && product.storeId) {
        const currentStoreId = cart[0].storeId;
        if (currentStoreId && currentStoreId !== product.storeId) {
            const confirmReset = confirm(`Bạn chỉ có thể đặt món ăn từ một cửa hàng trong cùng một đơn hàng. Bạn có muốn xóa giỏ hàng hiện tại (chứa món của ${cart[0].storeName}) để bắt đầu đặt đơn từ nhà hàng này không?`);
            if (confirmReset) {
                cart = []; // Clear current cart
            } else {
                return; // Do nothing
            }
        }
    }

    const existing = cart.find(c => c.id === productId);

    if (existing) {
        existing.quantity += 1;
    } else {
        cart.push({ ...product, quantity: 1 });
    }

    saveCart();
    updateCartUI();

    if (typeof gsap !== 'undefined') {
        gsap.fromTo('#cart-icon', { scale: 1.5 }, { scale: 1, duration: 0.3, ease: 'back.out' });
    }
}

window.removeFromCart = function (productId) {
    cart = cart.filter(c => c.id !== productId);
    saveCart();
    updateCartUI();
}

window.changeQuantity = function (productId, amount) {
    const item = cart.find(c => c.id === productId);
    if (item) {
        item.quantity += amount;
        if (item.quantity <= 0) {
            removeFromCart(productId);
        } else {
            saveCart();
            updateCartUI();
        }
    }
}

function saveCart() {
    localStorage.setItem('cart', JSON.stringify(cart));
}

function updateCartUI() {
    const cartBadge = document.getElementById('cart-badge');
    const cartCount = document.getElementById('cart-count');
    const container = document.getElementById('cart-items');
    const emptyMsg = document.getElementById('empty-cart-msg');
    const cartTotal = document.getElementById('cart-total');

    if (!cartBadge || !cartCount || !container || !emptyMsg || !cartTotal) return;

    const totalCount = cart.reduce((sum, item) => sum + item.quantity, 0);
    cartBadge.innerText = totalCount;
    cartCount.innerText = `(${totalCount})`;

    if (cart.length === 0) {
        emptyMsg.style.display = 'block';
        container.querySelectorAll('.cart-item').forEach(i => i.remove());
        cartTotal.innerText = '0 ₫';
        return;
    }

    emptyMsg.style.display = 'none';
    container.querySelectorAll('.cart-item').forEach(i => i.remove());

    let total = 0;
    cart.forEach(item => {
        total += item.price * item.quantity;
        const div = document.createElement('div');
        div.className = 'cart-item flex gap-4 bg-black p-3 rounded-xl border border-gray-800 mb-4';
        div.innerHTML = `
            <img src="${item.image}" class="w-16 h-16 rounded-lg object-cover">
            <div class="flex-1">
                <h4 class="font-bold text-sm mb-1">${item.name}</h4>
                <div class="text-brand-orange font-semibold mb-2">${formatPrice(item.price)}</div>
                <div class="flex items-center gap-3 bg-gray-900 w-max rounded-full px-2 py-1">
                    <button onclick="changeQuantity(${item.id}, -1)" class="w-6 h-6 flex items-center justify-center rounded-full bg-gray-800 hover:bg-brand-orange text-white hover:text-black transition-colors"><i class="fa-solid fa-minus text-xs"></i></button>
                    <span>${item.quantity}</span>
                    <button onclick="changeQuantity(${item.id}, 1)" class="w-6 h-6 flex items-center justify-center rounded-full bg-gray-800 hover:bg-brand-orange text-white hover:text-black transition-colors"><i class="fa-solid fa-plus text-xs"></i></button>
                </div>
            </div>
            <button onclick="removeFromCart(${item.id})" class="text-gray-500 hover:text-red-500 transition-colors h-max"><i class="fa-solid fa-trash"></i></button>
        `;
        container.appendChild(div);
    });

    cartTotal.innerText = formatPrice(total);
}

// --- GSAP ANIMATIONS ---
function initGSAP() {
    if (typeof gsap === 'undefined' || typeof ScrollTrigger === 'undefined') return;
    gsap.registerPlugin(ScrollTrigger);

    const tl = gsap.timeline();
    tl.from('#hero-content h1', { y: 50, opacity: 0, duration: 0.8, ease: 'power3.out' })
        .from('#hero-content p', { y: 30, opacity: 0, duration: 0.6, ease: 'power3.out' }, "-=0.4")
        .from('#hero-content .relative', { y: 20, opacity: 0, duration: 0.6 }, "-=0.2")
        .from('#hero-content .flex', { y: 20, opacity: 0, duration: 0.6 }, "-=0.4")
        .from('#hero-image-wrapper', { scale: 0.8, opacity: 0, duration: 1, ease: 'back.out' }, "-=0.8");

    document.querySelectorAll('.section-title').forEach(el => {
        gsap.from(el, {
            scrollTrigger: {
                trigger: el,
                start: "top 85%",
            },
            y: 40,
            opacity: 0,
            duration: 0.8,
            ease: 'power3.out'
        });
    });

    gsap.from('.category-card', {
        scrollTrigger: {
            trigger: '#category-list',
            start: "top 80%",
        },
        y: 50,
        opacity: 0,
        duration: 0.6,
        stagger: 0.1,
        ease: 'power2.out'
    });
}

// --- THREE.JS BACKGROUND (Bầu trời & Hạt bụi) ---
function initThreeJS() {
    const container = document.getElementById('canvas-container');
    if (!container || typeof THREE === 'undefined') return;

    const scene = new THREE.Scene();
    const camera = new THREE.PerspectiveCamera(75, window.innerWidth / window.innerHeight, 0.1, 1000);
    const renderer = new THREE.WebGLRenderer({ alpha: true, antialias: true });

    renderer.setSize(window.innerWidth, window.innerHeight);
    renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    container.innerHTML = '';
    container.appendChild(renderer.domElement);

    const particleGeometry = new THREE.BufferGeometry();
    const particleCount = 1500;
    const posArray = new Float32Array(particleCount * 3);
    for (let i = 0; i < particleCount * 3; i++) posArray[i] = (Math.random() - 0.5) * 40;
    particleGeometry.setAttribute('position', new THREE.BufferAttribute(posArray, 3));

    const particleMaterial = new THREE.PointsMaterial({
        size: 0.1, color: 0xff6b00, transparent: true, opacity: 0.8, blending: THREE.AdditiveBlending
    });

    const particlesMesh = new THREE.Points(particleGeometry, particleMaterial);
    scene.add(particlesMesh);
    camera.position.z = 5;

    let targetX = 0, targetY = 0;
    document.addEventListener('mousemove', (e) => {
        targetX = (e.clientX - window.innerWidth / 2) * 0.001;
        targetY = (e.clientY - window.innerHeight / 2) * 0.001;
    });

    function animate() {
        requestAnimationFrame(animate);
        particlesMesh.rotation.y += (targetX - particlesMesh.rotation.y) * 0.05;
        particlesMesh.rotation.x += (targetY - particlesMesh.rotation.x) * 0.05;
        renderer.render(scene, camera);
    }
    animate();

    window.addEventListener('resize', () => {
        camera.aspect = window.innerWidth / window.innerHeight;
        camera.updateProjectionMatrix();
        renderer.setSize(window.innerWidth, window.innerHeight);
    });
}
