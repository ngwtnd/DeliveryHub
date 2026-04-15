const categories = [
    { id: 'pizza', name: 'Pizza', icon: 'fa-pizza-slice', img: 'https://images.unsplash.com/photo-1565299624946-b28f40a0ae38?q=80&w=400&h=400&auto=format&fit=crop' },
    { id: 'burger', name: 'Burger', icon: 'fa-hamburger', img: 'https://images.unsplash.com/photo-1568901346375-23c9450c58cd?q=80&w=400&h=400&auto=format&fit=crop' },
    { id: 'sushi', name: 'Sushi', icon: 'fa-fish', img: 'https://images.unsplash.com/photo-1579871494447-9811cf80d66c?q=80&w=400&h=400&auto=format&fit=crop' },
    { id: 'pasta', name: 'Mì Ý', icon: 'fa-bowling-ball', img: 'https://images.unsplash.com/photo-1621996346565-e3dbc646d9a9?q=80&w=400&h=400&auto=format&fit=crop' },
    { id: 'salad', name: 'Salad', icon: 'fa-leaf', img: 'https://images.unsplash.com/photo-1512621776951-a57141f2eefd?q=80&w=400&h=400&auto=format&fit=crop' },
    { id: 'drinks', name: 'Đồ uống', icon: 'fa-wine-glass', img: 'https://images.unsplash.com/photo-1544145945-f90425340c7e?q=80&w=400&h=400&auto=format&fit=crop' }
];

let products = [
    {
        id: 1,
        categoryId: 'burger',
        name: 'The Boss Burger',
        price: 120000,
        description: 'Bò Mỹ 100%, phô mai cheddar tan chảy, rau xà lách tươi và sốt đặc biệt.',
        image: 'https://images.unsplash.com/photo-1568901346375-23c9450c58cd?q=80&w=600&auto=format&fit=crop',
        rating: 4.8
    },
    {
        id: 2,
        categoryId: 'pizza',
        name: 'Pizza Hải Sản Phô Mai',
        price: 250000,
        description: 'Mực, tôm tươi ngon cùng nền phô mai Mozzarella béo ngậy.',
        image: 'https://images.unsplash.com/photo-1565299624946-b28f40a0ae38?q=80&w=600&auto=format&fit=crop',
        rating: 4.9
    },
    {
        id: 3,
        categoryId: 'sushi',
        name: 'Sashimi Cá Hồi',
        price: 180000,
        description: 'Cá hồi Na Uy tươi nhập khẩu, dùng kèm mù tạt và nước tương cay.',
        image: 'https://images.unsplash.com/photo-1579871494447-9811cf80d66c?q=80&w=600&auto=format&fit=crop',
        rating: 4.7
    },
    {
        id: 4,
        categoryId: 'pasta',
        name: 'Spaghetti Carbonara',
        price: 150000,
        description: 'Sốt kem béo ngậy chuẩn Ý, thịt lợn xông khói và phô mai Parmesan rắc nhẹ.',
        image: 'https://images.unsplash.com/photo-1621996346565-e3dbc646d9a9?q=80&w=600&auto=format&fit=crop',
        rating: 4.5
    },
    {
        id: 5,
        categoryId: 'salad',
        name: 'Salad Gà Nướng Mật Ong',
        price: 95000,
        description: 'Thức ăn healthy với ức gà nướng mềm, rau mầm tươi mát và oliu.',
        image: 'https://images.unsplash.com/photo-1512621776951-a57141f2eefd?q=80&w=600&auto=format&fit=crop',
        rating: 4.6
    },
    {
        id: 6,
        categoryId: 'drinks',
        name: 'Sinh Tố Nhiệt Đới',
        price: 65000,
        description: 'Hỗn hợp xoài, dứa, cam tươi xay mịn giải khát tuyệt đối.',
        image: 'https://images.unsplash.com/photo-1544145945-f90425340c7e?q=80&w=600&auto=format&fit=crop',
        rating: 4.8
    }
];

const testimonials = [
    {
        name: 'Nguyễn Văn A',
        avatar: 'https://randomuser.me/api/portraits/men/32.jpg',
        comment: 'Tuyệt vời, đặt burger 15p đã nhận được, bánh vẫn còn nóng giòn và rất ngon!',
        rating: 5
    },
    {
        name: 'Trần Thị B',
        avatar: 'https://randomuser.me/api/portraits/women/44.jpg',
        comment: 'Sushi rất tươi, điểm cộng là bao bì đóng gói đẹp và sang, rất đáng tiền.',
        rating: 5
    },
    {
        name: 'Lê Minh C',
        avatar: 'https://randomuser.me/api/portraits/men/85.jpg',
        comment: 'Giao diện đặt hàng cực mượt, tốc độ xuất sắc. Sẽ ủng hộ dài dài.',
        rating: 4
    },
    {
        name: 'Hoàng Oanh',
        avatar: 'https://randomuser.me/api/portraits/women/12.jpg',
        comment: 'Pizza ngon đỉnh, shipper nhiệt tình. Khuyến mãi liên tục trên app.',
        rating: 5
    }
];
